using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO.Compression;
using System.Security.Principal;
using System.Timers;
using eKassir.Terminal.XSearch.Helpers;
using eKassir.Terminal.XSearch.Properties;
using IBP.ExternalLibrary;
using SearchEngine;
using System.Threading;
using System.Reflection;
using Newtonsoft.Json.Linq;
using System.IO;

namespace eKassir.Terminal.XSearch
{
    public class TerminalXSearch : ExternalLib
    {
        /// ***ВНИМАНИЕ!!! Для упаковки зависимых библиотек в сборку используется LinqPad скрипт в \libs\pack_libs_with_linqpad.linq***
        /// В Build-Events
        /// (Pre-Build):
        /// "$(SolutionDir)libs\LPRun.exe" pack_libs_with_linqpad.linq
        ///    copy "$(SolutionDir)libs\Lucene.Net.dll.deflate" "$(SolutionDir)eKassir.Terminal.XSearch\Resources\"
        ///    copy "$(SolutionDir)libs\Newtonsoft.Json.dll.deflate" "$(SolutionDir)eKassir.Terminal.XSearch\Resources\"
        /// (Post-Build):
        /// copy eKassir.Terminal.XSearch.dll "$(SolutionDir)!Output\"
        /// 
        /// 

        //таймер для обновления индекса (по умолчанию 60 минут)
        System.Timers.Timer _timer;
        const string _period = "60";
        //дополнительные атрибуты услуги для поиска
        //string[] _additionalAttrsName = {"''"};
        string[] _additionalAttrsName = { string.Empty };  
        //разделители для парсинга из настроек
        char[] _delimeters = {',', ';'};
        //убираем лишние знаки из запроса от JSON-а
        //char[] charsToTrim = { '"', ' ', '\'' };
        //char[] charsToTrim = { '"', '\\' };
        char[] charsToTrim = string.Empty.ToCharArray();
        //алиас для вызова услуги по умолчанию (в случае если ищем только по базе и в сеттинге нет алиаса для вызова)
        private static string _alias = string.Empty;
        //так называется атрибут строка с запросом в json-объекте
        const string searchString = "searchstr";
        //имя массива с результатами поиска в json-объекте
        const string xArray = "xarray";
        //имя файла сеттинга
        const string settingsPath = "settings_.xml";

        //домен для поиска сборок сторонних библиотек        
        AppDomain root = AppDomain.CurrentDomain;

        //для UNIT-тестов
        //string settingsPath = @".\!Output\settings_leto.xml";                
        //инстанс поискового движка
        SearchEngine.XSearch _xInstance;
        //object _xInstance = null;

        //TODO сделать без коллекций, напрямую в индекс, подкорректировать методы либы
        //результат выборки услуги из БД
        List<SearchEngine.XSearchObj> _searchList;

        string GetSqlqueryService(string searchFields)
        {
            String cmd = string.Format("IF EXISTS (SELECT TOP(1) _value FROM extension_objects WHERE _name IN ({0})) BEGIN SELECT s.id_service, s.alias, s.name_service, s.logo, version, ext._value searchTags FROM service s JOIN extension_objects ext ON s.id_service = ext.idd_object WHERE ext._name IN ({1}) END ELSE BEGIN SELECT id_service, alias, name_service, logo, version, name_service as searchTags FROM service END", searchFields, searchFields);

            return cmd;
        }



        public TerminalXSearch()
        {

            //прицепимся к событию определения сборки
            //root.AssemblyLoad += AppDomain_AdditionLibrariesAssemblyLoad;
            root.AssemblyResolve -= AppDomain_AdditionLibrariesAssemblyResolve;
            root.AssemblyResolve += AppDomain_AdditionLibrariesAssemblyResolve;  
    
            _settings = new Setting();
            _timer = new System.Timers.Timer();
            _timer.Elapsed += new ElapsedEventHandler(RefreshIndexEvent);
            SetPrefixForLogger("XSearch");

            //_xInstance = SearchEngine.XSearch.GetInstance;
        }

        // Событие возникающее при невозможности найти сборку (распаковывает из ресурсов нужные dll)
        private static Assembly AppDomain_AdditionLibrariesAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var one_megabyte = 1024 * 1024;
            //var two_megabytes = 2096 * 1024;
            byte[] buffer = null;
            Assembly rtnAssembly = null;

            //rtnAssembly = Assembly.GetExecutingAssembly();

            if (args.Name.Contains("Lucene"))
            {
                // Загрузка запакованной сборки из ресурсов, ее распаковка и подстановка
                using (var resource = new MemoryStream(eKassir.Terminal.XSearch.Properties.Resources.Lucene_Net_dll))
                using (var deflated = new DeflateStream(resource, CompressionMode.Decompress))
                using (var reader = new BinaryReader(deflated))
                {
                    buffer = reader.ReadBytes(one_megabyte);
                    //return Assembly.Load(buffer);
                }
            }
            if (args.Name.Contains("Newtonsoft"))
            {
                using (var resource = new MemoryStream(eKassir.Terminal.XSearch.Properties.Resources.Newtonsoft_Json_dll))
                using (var deflated = new DeflateStream(resource, CompressionMode.Decompress))
                using (var reader = new BinaryReader(deflated))
                {
                    buffer = reader.ReadBytes(one_megabyte);
                    //return Assembly.Load(buffer);
                }
            }

            rtnAssembly = Assembly.Load(buffer);
            return rtnAssembly;
            //return null;
        }


        class AsynkContext
        {
            public string MethodName { get; set; }
            public object Parameter { get; set; }
        }

        public class Setting : ExternalLibAbsSettings
        {
            //public Setting()
            //{
            //    Period = _period;
            //    SearchAttrs = string.Empty;
            //    WithoutMenu = string.Empty;

            //    AppDomain root = AppDomain.CurrentDomain;
            //    //прицепимся к событию определения сборки
            //    root.AssemblyResolve += AppDomain_AdditionLibrariesAssemblyResolve;
            //}

            [ExternalLibSetting("Index refresh period (in minutes)")]
            public string Period { get; set; }

            //дополнительные атрибуты в PaySystem для поиска в услуге
            [ExternalLibSetting("List of attributes to search")]
            public string SearchAttrs { get; set; }

            //искать только в базе, исключая меню в setting по данному scen_alias-у
            [ExternalLibSetting("Search without menu (input scen_alias)")]
            public string WithoutMenu { get; set; }
        }

        #region Private methods    

        //private static void AppDomain_AdditionLibrariesAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        //{
        //    //AppDomain_AdditionLibrariesAssemblyResolve(sender, args);
        //    //return null;
        //}

   


        private Setting XSetting
        {
            get { return (Setting)this._settings; }
        }  

        //считываем все из таблицы services
        ReaderCallback _callBackQueryServices = delegate(SqlDataReader reader)
        {                
                SearchEngine.XSearch tempInstance = SearchEngine.XSearch.GetInstance;

                SearchEngine.XSearchObj temp = new SearchEngine.XSearchObj();
                temp.id_service   = reader[0].ToString().ToCharArray();
                temp.alias        = reader[1].ToString().ToCharArray();
                temp.name_service = reader[2].ToString().ToCharArray();
                temp.logo         = reader[3].ToString().ToCharArray();
                temp.version      = reader[4].ToString().ToCharArray();
                temp.search_tag   = reader[5].ToString().ToCharArray();
                //сценарий для вызова услуги по-умолчанию                
                temp.scen_alias = _alias.ToCharArray();
                //temp.search_tag = (reader[5].ToString() + " "+ reader[6].ToString()).ToCharArray();                                

                //searchList.Add(temp);
                tempInstance.AddDocument(temp);
                                       
            //парсим setting, заносим оттуда услуги в индекс и обновляем его
            //xInstance.ParseSetting(settingsPath);                        
        };

        //событие обновления индекса по услугам
        void RefreshIndexEvent(object source, ElapsedEventArgs e)
        {
            RefreshIndex();
        }

        //обновление индекса услуг        
        private void RefreshIndex() {
                        
            //получаем инстанс поискового движка (из-за ленивой инициализации синглтона память и время не будут расходоваться)
            _xInstance = SearchEngine.XSearch.GetInstance;


            List<SearchEngine.XSearchObj> fromSetting = new List<SearchEngine.XSearchObj>();

            //если включено исключение ограничения меню по поиску
            bool withoutMenu = false;
            withoutMenu = !String.IsNullOrEmpty(XSetting.WithoutMenu);
            _alias = !withoutMenu ? string.Empty : XSetting.WithoutMenu;

            //if (!withoutMenu)
            {
                //парсим сеттинг (внимание, изменяется WhereIn в xInstance, его подставим в выражение ниже!!!)
                fromSetting = _xInstance.ParseSetting(settingsPath);
            }

            _xInstance.OpenWriter();

            //"SearchTags" - поле в базе SQL которое совмещает в себе название услуги и ключевые слова по которым ищем            
            SqlCommand sqlCommand = new SqlCommand(GetSqlqueryService(string.Join(",", _additionalAttrsName)));

            //обновляем индекс            
            ExecuteSqlQuery(sqlCommand, _callBackQueryServices);

            //закрываем Writer индекса
            _xInstance.CloseWriter();

            //добавляем документы (услуги) в индекс
            //_xInstance.AddDocuments(ref searchList, true, false);   

            //если включено исключение ограничения меню по поиску
            //if (!withoutMenu)
            {
                //объединяем результаты парсинга сеттинга и индекса
                _xInstance.MergeWithScenAliasList(fromSetting, withoutMenu);
            }

            //очищаем накопленные услуги
            _searchList.Clear();

            Logger("Index refreshed");
        }
                

        #endregion

        #region ExternalLib methods


        public override void Open(string param)
        {            

            Logger("Starting XSearch plugin v" + Assembly.GetExecutingAssembly().GetName().Version);

            ushort _refreshInterval = 0;

            try
            {

                _searchList = new List<SearchEngine.XSearchObj>();


                //считываем доп атрибуты для поиска из настроек ()
                if (string.IsNullOrEmpty(XSetting.SearchAttrs))
                {
                    _additionalAttrsName = Helper.AddQuotes(_additionalAttrsName);
                    Logger("List of attributes is set to empty values");
                }
                else
                    _additionalAttrsName = Helper.AddQuotes(XSetting.SearchAttrs.Split(_delimeters));  

                //считываем время обновления индекса (в минутах)                    
                if (string.IsNullOrEmpty(XSetting.Period))
                {
                    _refreshInterval = Convert.ToUInt16(_period);
                    Logger("Refresh interval set by default value cause XSearch init error: " + _refreshInterval +
                           " minutes");
                }
                else
                    _refreshInterval = Convert.ToUInt16(XSetting.Period);

                //задаем сценарий (alias) для тех услуг которые вызываются без <menuitem alias=>
                _alias = XSetting.WithoutMenu;

                //больше чем 1440 минут в сутках быть не может
                if (_refreshInterval > 1440)
                {
                    _refreshInterval = 1440;
                }
                else if (_refreshInterval <= 0)
                {
                    _refreshInterval = 1;
                }
 
            }
            catch (Exception ex)
            {
                if (ex is FormatException)
                {
                    Logger("Settings conversion error: " + ex.Message);
                    //Logger("Settings conversion error: " + exceptionString);
                    //throw new FormatException(ex.Message);
                }
                if (ex is OverflowException)
                {
                    Logger("Period Overflow: " + ex.Message);
                    //Logger("Period Overflow: " + exceptionString);
                    //throw new OverflowException(ex.Message);
                }
                if (ex is NullReferenceException)
                {
                    Logger("Error reading plugin settings: " + ex.Message);
                    //Logger("Error setting refreshInterval: " + exceptionString);
                }
                else
                    Logger("Error starting XSearch plugin: " + ex.Message + ex.StackTrace);
            }


            try
            {

                XSetting.Period = _refreshInterval.ToString(CultureInfo.InvariantCulture);
                _timer.Interval = _refreshInterval * 60000; //переводим минуты в милисекунды                

                //делаем первый рефреш
                RefreshIndex();
                _timer.Start();

                Logger("XSearchIndex refresh interval: " + _refreshInterval + " minutes");

            }
            catch (Exception ex)
            {
                Logger("Error starting XSearch refresh: " + ex.Message + ex.StackTrace);
            }
        }


        //переопределяем и задаем методы Externallib
        public override string Method(string jsonString)
        {

            JObject obj = (JObject)JObject.Parse(jsonString);
            string methname = obj["method"].ToString();
            JObject parametrs = (JObject)obj["params"];
            //string parametrs = (string)obj["params"];

            try
            {
                ThreadPool.QueueUserWorkItem(AsynkMethod, new AsynkContext { MethodName = methname, Parameter = parametrs });
                JObject ret = new JObject();
                ret["asynk"] = true;
                return ret.ToString();

            }
            catch (Exception ex)
            {
                Logger(string.Format("Method \"{0}\" executing with error:{2}{1}", methname, ex, Environment.NewLine));
                return string.Empty;
            }
        }

        public override void Close(string param)
        {
            _timer.Stop();
            _timer.Elapsed -= new ElapsedEventHandler(RefreshIndexEvent);
            //очищаем накопленные услуги
            _searchList.Clear();
        }

        //основной метод поиска
        public JObject Search(JObject JsearchString)
        {

            string result = string.Empty;
            JObject objResult = new JObject();

            //JavaScriptSerializer ser = new JavaScriptSerializer();

            Logger("Calling xsearch method");

            try
            {
                //инстанс поискового движка
                _xInstance = SearchEngine.XSearch.GetInstance;

                //TODO сделать проверку на пустую строку поиска нет "searchstring" и значения, например возникает когда двигаемся назад в поиск после выбора услуги
                string query = string.Empty;

                //JsearchString.TryGetValue(searchString, out query);

                query = JsearchString[searchString].ToString().Trim(charsToTrim);                

                //делегат для асинхронного вызова
                SearchEngine.AsyncSearch asearch = new SearchEngine.AsyncSearch(_xInstance.Search);
                
                //сделано синхронно для UI, можно добавить Callback для асинхронного запроса
                IAsyncResult res = asearch.BeginInvoke(query, null, null);

                res.AsyncWaitHandle.WaitOne();

                // вызываем EndInvoke чтобы получить результат.
                List<SearchEngine.XSearchObjJSON> searchResult = asearch.EndInvoke(res);

                // Close the wait handle.
                res.AsyncWaitHandle.Close();

                //сериализуем в json
                objResult[xArray] = JToken.FromObject(searchResult);
            }
            catch (MissingMethodException)
            {
                String message = "Incorrect search conditions: ";
                //result = ser.Serialize(message);
                objResult = (JObject)Newtonsoft.Json.JsonConvert.SerializeObject(message);
                Logger(message + searchString);
            }
            catch (Exception ex)
            {
                String message = "Fatal search error: ";
                //result = ser.Serialize(message);
                objResult = (JObject)Newtonsoft.Json.JsonConvert.SerializeObject(message);
                Logger(message + ex.Message);
            }

            return objResult;
        }

        public void AsynkMethod(object state)
        {
            Exception e = null;
            AsynkContext context = (AsynkContext)state;
            JObject inParams = context.Parameter as JObject;

            JObject ret = new JObject();

            try
            {
                MethodInfo meth = this.GetType().GetMethod(context.MethodName);
                ret["xresult"] = (JObject)meth.Invoke(this, new object[] { context.Parameter });
                if (inParams != null)
                {
                    ret["callbackname"] = inParams["callbackname"];
                }
                //раскоментить, если нужно выводить в лог найденные услуги
                //Logger(string.Format("Asynс Method: \"{0}\", Search string: \"{1}\", Result: \"{2}\" ", context.MethodName, inParams[searchString].ToString(), ret.ToString()));                
                this.CallBack("asynkcallback", ret.ToString());
            }
            catch (TargetInvocationException ex)
            {
                ret["error"] = 401;
                e = ex.InnerException;
                Logger(string.Format("Asynс Method: \"{0}\". (asynс Invoke TargetInvocationException) Result: \"{1}\" Ex: \"{2}\"", context.MethodName, ret.ToString(), ex.ToString()));
            }
            catch (Exception ex)
            {
                e = ex;
                ret["error"] = 501;
                Logger(string.Format("Asynс Method: \"{0}\". (asynс Invoke Exception)  Result: \"{1}\" Ex: \"{2}\"", context.MethodName, ret.ToString(), ex.ToString()));
            }
            if (e != null)
            {
                if (inParams != null)
                {
                    ret["callbackname"] = inParams["callbackname"];
                }
                this.CallBack("asynkcallback", ret.ToString());
            }
        }

        public override void TaskMethod(string nameTask, Dictionary<string, string> prms)
        {
            throw new NotImplementedException();
        }


        public override void BarcodeReceived(string barcode)
        {
            throw new NotImplementedException();
        }

        public override void BillStacked(decimal banknote)
        {
            throw new NotImplementedException();
        }
        

        #endregion

    }

    
}
