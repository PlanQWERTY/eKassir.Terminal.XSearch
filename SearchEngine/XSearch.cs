using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using eKassir.Terminal.XSearch.Helpers;
//using Lucene.Net;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Analysis;
using Lucene.Net.QueryParsers;
using Directory = Lucene.Net.Store.Directory;
using Version = Lucene.Net.Util.Version;

namespace SearchEngine
{

    //делегат для асинхронного Search
    public delegate List<XSearchObjJSON> AsyncSearch(string queryString);

    //задан как sinlgleton с ленивой инициализацией
    public class XSearch
    {
        //Защищенный конструктор нужен, чтобы предотвратить создание экземпляра класса XSearch
        protected XSearch() { InitMainIndex(); }

        private sealed class XSearchCreator
        {

            private static readonly XSearch instance = new XSearch();
            public static XSearch Instance { 
                get {                    
                    return instance;
                }
            }
        }

        public static XSearch GetInstance
        {
            get
            {  
                return XSearchCreator.Instance;
            }
        }

        #region ****private****
        //блокирем обновление индекса от поиска и много чего еще)
        private Object searchLock = new Object();
        IndexWriter GlobalWriter = null;        
        //путь для основного индекса на диске
        const string GlobalPath = @"XIndex";
        //const string LUCENE_ESCAPE_CHARS = "[\\\\+\\-\\!\\(\\)\\:\\^\\]\\{\\}\\~\\*\\?]";
        //const string REPLACEMENT_STRING = "\\\\$0"; 
        const string LUCENE_ESCAPE_CHARS = @"[\+\-\!\(\)\:\^\]\{\}\~\?]";
        const string REPLACEMENT_STRING = @"\$0"; 
        //меняем имя расширения если вдруг в ui опять что-то изменится для построения меню результатов поиска (меню строится только с расширением, а услуги могут только по названию)  
        const string imagesExtention = "";
        //const string imagesExtention = @".png";
        //заведем объект только для чтения полей
        static XSearchObj read_xsobj_field = new XSearchObj();
        //основное поле для полнотекстового поиска (вытаскиваем имя поля из спец. созданного объекта)
        string SearchField = Helper.GETNAME(new { read_xsobj_field.search_tag }); // "search_tag";
        //первоначальный запуск обновления индекса
        //bool IndexUpdated = false;
        //глбальные пути для памяти и каталога
        Directory _catalogPath, _memoryPath;
        //количество совпадений (количество услуг которое выводится для терминала, изначально было 12 в рамках совместимости)
        const int maxHitsPerSearch = 5000;
        //минимальное допустимое количество символов для поисковой строки, поставить 0 если нужно разрешить любое количество
        const int minSymbPerSearch = 1; 

        #endregion

        //****Методы класса****//        

        //проверяем создан ли индекс в директории
        bool IndexExists (ref Directory dir) { return dir.FileExists("segments.gen"); }

        //Punctuation = ("_"|"-"|"/"|"."|",") по которым lucene разбивает запрос на термы, но вариант "мтс. что-то там" не работает
        //динамически создаем анализатор запросов (используем версию 3.0.3)

        /*
         * Заменили на WhitespaceAnalyzer и его производную так как был кейс от 14.06:
         * По поиску твоей либы xSearch еще один баг увидели.
        Если ввести значение для поиска, содержащее пробел, то результат будет "ничего не найдено".
        searchstr = инн 1 
            "xresult": {
            "xarray": []
            },
        Заведено 5 услуг инн 101, инн 102, инн 103, инн 104, инн 105, и все выводятся по "инн"*/

        //Analyzer analyzer { get { return new WhitespaceAnalyzer(); } }

        /*
         * Заменили на производную WhitespaceAnalyzer и его  так как был кейс от 03.07 с незапуском на POSReady 2009
         */

        //Analyzer analyzer { get { return new CaseInsensitiveWhitespaceAnalyzer(); } }
        Analyzer analyzer { get { return new StandardAnalyzer(Version.LUCENE_30); } }
        //Analyzer analyzer { get { return new SimpleAnalyzer(); } }

        //чтобы подключить русский с расширенной семантикой, нужно компилить модуль отдельно (исходники есть) либо ставить Lucene.Contrib
        //Analyzer analyzer { get { return new RussianAnalyzer(Version.LUCENE_30); } } 

        

        //создаем индекс в каталоге и в памяти (нужно для сравнения и переноса из памяти на диск)
        void InitMainIndex()
        {
            lock (searchLock)
            {
                try
                {
                    //в памяти
                    _memoryPath = new RAMDirectory();
                    //на диске                
                    _catalogPath = FSDirectory.Open(GlobalPath);
                }
                catch (Exception ex)
                {
                    //Console.WriteLine("Error creating Index! \n" + ex.Message);
                    throw new Exception("Error creating Index! \n" + ex.Message);
                }
            }
        }

        //открываем глобальный Index Writer (для считывания из БД по одной записи)
        public void OpenWriter() {
            if (null != _catalogPath)
            {
                lock (searchLock)
                {
                    GlobalWriter = new IndexWriter(_catalogPath, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);
                }            
            }
        }

        //закрываем глобальный Index Writer
        public void CloseWriter()
        {
            if (null != GlobalWriter)
                GlobalWriter.Dispose();
        }

        
        //инициализируем структуры
        IndexWriter Initialize(ref Directory indexDir, bool delAll, bool toMemory)
        {
                IndexWriter writer = null;

                if (toMemory)
                {
                    writer = new IndexWriter(indexDir, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);
                }

                else
                {
                    bool indexNotExists = !IndexExists(ref indexDir); //проверка на уже существующий индекс

                    writer = new IndexWriter(indexDir, analyzer,
                    indexNotExists, IndexWriter.MaxFieldLength.UNLIMITED); //либо создаем новый индекс (true) либо работаем со старым (false)                 

                    //если индекс уже есть в папке
                    if (!indexNotExists && delAll)
                    {   //TODO: реализовать проверку на схожесть индексов или удаление дубликатов при слиянии (оказалось весьма непросто)
                        writer.DeleteAll();  //удаляем индекс если он уже есть
                    }
                }
                return writer;          
        }

        //вспомогательный метод для парсинга Xml
        private void fillXmlItem(bool menu, ref List<XSearchObj> scenAliasList, ref string scen_alias, ref string service_alias)
        {
            XSearchObj fromMenuitem = new XSearchObj();

            if (scen_alias != null && service_alias == null)
            {
                fromMenuitem.scen_alias = scen_alias.ToCharArray();
                //так как service_alias пуст, добавим в service_alias псевдоним сценария
                fromMenuitem.alias = scen_alias.ToCharArray();
                //добавление в коллекцию LuceneIndex либо в коллекцию
                scenAliasList.Add(fromMenuitem);
            }
            else if (scen_alias != null && service_alias != null)
            {
                fromMenuitem.alias = (menu) ? service_alias.ToCharArray() : scen_alias.ToCharArray();
                fromMenuitem.scen_alias = (menu) ? scen_alias.ToCharArray() : service_alias.ToCharArray();
                //добавление в коллекцию LuceneIndex либо в коллекцию
                scenAliasList.Add(fromMenuitem);
            }
        }

        //*************парсим setting.xml для поиска имен сценариев ассоциированных услугам*****************
        //TODO: убрать лишние XSearchObj fromScenario = new XSearchObj(); использовать глобальный объект для добавления (который для чтения имени полей)
        public List<XSearchObj> ParseSetting(string path)
        {            
            //либо создаем коллекцию для накопления информации по алиасам сценариев
            List<XSearchObj> scenAliasList = new List<XSearchObj>();

            using (XmlReader xml = XmlReader.Create(path))
            {
                while (xml.Read())
                {
                    switch (xml.NodeType)
                    {
                        case (XmlNodeType.Element):

                            //нашли элемент step и в нем атрибут MainMenu
                            if (xml.Name == "step" && xml.GetAttribute("name") == "MainMenu")
                            {
                                XmlReader mainMenuNode = xml.ReadSubtree();                                

                                while (mainMenuNode.Read())
                                {
                                    // нашли элемент menuitem и избавляемся от оболочек в меню с type="1", которые скрывают реальные услуги
                                    if (mainMenuNode.Name == "menuitem" && xml.HasAttributes && xml.GetAttribute("type") == "0")
                                    {                                            
                                            string scen_alias = null, service_alias = null;
                                            scen_alias = xml.GetAttribute("alias");
                                            service_alias = xml.GetAttribute("service_alias");
                                            
                                            fillXmlItem(true, ref scenAliasList, ref scen_alias, ref service_alias);                                                                                                                             
                                    }
                                }

                                mainMenuNode.Close();
                            }
                            

                            // нашли элемент scenario
                            if (xml.Name == "scenario" && xml.HasAttributes)
                            {
                                string scen_alias = null;
                                scen_alias = xml.GetAttribute("alias");

                                if (scen_alias.Length != 0)
                                {
                                        XSearchObj fromScenario = new XSearchObj();
                                        fromScenario.scen_alias = scen_alias.ToCharArray();
                                        //так как service_alias пуст, добавим в service_alias псевдоним сценария
                                        fromScenario.alias = scen_alias.ToCharArray();
                                        //добавление в коллекцию LuceneIndex либо в коллекцию
                                        scenAliasList.Add(fromScenario);
                                    }   
                            }

                            // нашли элемент scenarioIntricate
                            if (xml.Name == "scenarioIntricate" && xml.HasAttributes)
                            {
                                    string serv_alias = null, scen_alias = null;
                                    serv_alias = xml.GetAttribute("name");
                                    scen_alias = xml.GetAttribute("alias");

                                    fillXmlItem(false, ref scenAliasList, ref serv_alias, ref scen_alias);

                            }
                            //нужно бросить исключение что ничего не найдено???                          
                            break;
                    }
                }
            }

            return scenAliasList;
        }

        //создаем "документ" в терминологии Lucene (как элемент поиска)
        Document CreateDocument(ref XSearchObj service)
        {
            Document doc = new Document();

            doc.Add(new Field(Helper.GETNAME(new { service.id_service }), new string(service.id_service), Field.Store.YES, Field.Index.NOT_ANALYZED)); //(индексируется как id)
            doc.Add(new Field(Helper.GETNAME(new { service.alias }), new string(service.alias), Field.Store.YES, Field.Index.NOT_ANALYZED)); //(индексируется без анализа)
            doc.Add(new Field(Helper.GETNAME(new { service.scen_alias }), new string(service.scen_alias), Field.Store.YES, Field.Index.NOT_ANALYZED)); //(индексируется без анализа)
            doc.Add(new Field(Helper.GETNAME(new { service.name_service }), new string(service.name_service), Field.Store.YES, Field.Index.NO));
            doc.Add(new Field(Helper.GETNAME(new { service.logo }), new string(service.logo), Field.Store.YES, Field.Index.NO)); //(не индексируется)
            doc.Add(new Field(Helper.GETNAME(new { service.version }), new string(service.version), Field.Store.YES, Field.Index.NO)); //(не индексируется)
            //*******!!!! Внимание индексируется без норм (если нужна морфология и провинутые яз. возможности, то включить)**********
            doc.Add(new Field(Helper.GETNAME(new { service.search_tag }), new string(service.search_tag), Field.Store.YES, Field.Index.ANALYZED_NO_NORMS));
            //*******!!!! Внимание ниже добавлено дополнительное поле для поиска по ключевым словам**********
            //doc.Add(new Field(Helper.GETNAME(new { service.name_service }), new string(service.name_service), Field.Store.YES, Field.Index.ANALYZED_NO_NORMS));

            return doc;
        }
        

        //TODO: возможность для автоматического добавления коллекций (с циклом?)!!!!!!!!!
        //добавляем "документ" для поиска в память или на диск
        public void AddDocuments(ref List<XSearchObj> service, bool delAll, bool toMemory)
        {
            IndexWriter writer = null;

            writer = toMemory ? Initialize(ref _memoryPath, delAll, toMemory) : Initialize(ref _catalogPath, delAll, toMemory);            

            try
            {
                foreach (XSearchObj obj in service)
                    AddDocument(ref writer, obj);
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException || ex is NullReferenceException)
                {
                    //закрываем writer                    
                    writer.Dispose();
                    //Console.WriteLine("Error adding document! \n" + ex.Message);
                    return;
                }
                throw new Exception("Error adding document! \n" + ex.Message);
            }

            writer.Dispose();
        }


        //для записи из БД по одной записи (обязательно делать WriterClose после всех записей!!!)
        public void AddDocument(XSearchObj service) {
            lock (searchLock)
            {
                try
                {
                    GlobalWriter.AddDocument(CreateDocument(ref service));
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException || ex is NullReferenceException)
                    {
                        //закрываем writer                    
                        GlobalWriter.Dispose();
                        return;
                    }
                    throw new Exception("Error adding document! \n" + ex.Message);
                }
            }
        }

        //для "внутреннего использования"
        void AddDocument(ref IndexWriter writer, XSearchObj service) {
            lock (searchLock)
            {
                try
                {
                    writer.AddDocument(CreateDocument(ref service));
                    //writer.UpdateDocument(new Term("id_service"), CreateDocument(ref service));
                    //writer.Dispose();
                }
                catch (Exception ex)
                {
                    if (ex is OutOfMemoryException || ex is NullReferenceException)
                    {
                        //закрываем writer                    
                        writer.Dispose();
                        //Console.WriteLine("Error adding document! \n" + ex.Message);
                        return;
                    }
                    throw new Exception("Error adding document! \n" + ex.Message);
                }
            }
        }
                  


        //ищем в индексе услуги по alias-у и дополняем в индексе поля с незаданным scen_alias (алиас сценария)        
        public void MergeWithScenAliasList(IEnumerable<XSearchObj> list, bool withoutMenu)
        {
            lock (searchLock)
            {
                IndexWriter catalogWriter = Initialize(ref _catalogPath, false, false);

                //IndexReader reader = IndexReader.Open(catalogPath, false); //ReadOnly = false
                IndexReader reader = catalogWriter.GetReader();

                IndexSearcher searcher = new IndexSearcher(reader);

                //поиск по alias-у                
                foreach (XSearchObj obj in list)
                {
                    //ищем есть ли в индексе услуга с соответвующим алиасом                
                    Term aliasTerm = new Term(Helper.GETNAME(new { obj.alias }), new string(obj.alias));
                    Query query = new TermQuery(aliasTerm);

                    //TODO: посмотреть все ли результаты отдает, если нет, писать свой CustomCollector (важно!!!)                    
                    TopDocs docs = searcher.Search(query, maxHitsPerSearch);
                    ScoreDoc[] hits = docs.ScoreDocs;


                    if (docs.TotalHits > 0)
                    {
                        //пробегаем по результатам и 
                        for (int i = 0; i < docs.TotalHits; i++)
                        {
                            ScoreDoc t = hits[i];

                            Document doc = searcher.Doc(t.Doc);
                            //int docId = t.Doc;

                            try
                            {
                                //***********удаляем старый документ (по запросу) !!!Если есть дубли может возникнуть ошибка в цикле, обработать!!!******
                                catalogWriter.DeleteDocuments(query);                                
                            }
                            catch (OutOfMemoryException)
                            {            
                                catalogWriter.Dispose();                                
                                throw;
                            }
                                                           

                            XSearchObj temp = new XSearchObj
                            {
                                id_service = doc.Get(Helper.GETNAME(new { obj.id_service })).ToCharArray(),
                                alias = doc.Get(Helper.GETNAME(new { obj.alias })).ToCharArray(),
                                scen_alias = obj.scen_alias,
                                name_service = doc.Get(Helper.GETNAME(new { obj.name_service })).ToCharArray(),
                                logo = doc.Get(Helper.GETNAME(new { obj.logo })).ToCharArray(),
                                version = doc.Get(Helper.GETNAME(new { obj.version })).ToCharArray(),
                                search_tag = doc.Get(Helper.GETNAME(new { obj.search_tag })).ToCharArray()
                            };

                            //Term scenAlias = new Term("scen_alias", obj.scen_alias.ToString());
                            //catalogWriter.UpdateDocument(scenAlias, doc);


                            //добавляем обновленный (с именем сценария)
                            catalogWriter.AddDocument(CreateDocument(ref temp));
                        }
                    }

                }


                if (!withoutMenu)
                {
                    //удаляем все из индекса что не содержит алиаса сценария
                    try
                    {
                        Term emptyScenAlias = new Term(Helper.GETNAME(new {read_xsobj_field.scen_alias}), String.Empty);
                        catalogWriter.DeleteDocuments(emptyScenAlias);
                    }

                    catch (Exception ex)
                    {
                        catalogWriter.Dispose();
                        throw ex;
                    }
                }

                Optimize(ref catalogWriter);
                Dispose(ref catalogWriter);


            }
        }       
        
        //ищем услугу в индексе. что ищем (queryString) и в каком поле (Field)
        //TODO: переделать на поиск по множеству полей и выдачу нужных полей (передача SearchObject?)
        public List<XSearchObjJSON> Search(string queryString)
        {
            lock (searchLock)
            {
                IndexReader reader = IndexReader.Open(_catalogPath, true); //ReadOnly = true для производительности
                IndexSearcher searcher = null;

                try
                {
                    //создаем список JSON объектов для сериализации
                    List<XSearchObjJSON> xsearchObjList = new List<XSearchObjJSON>();

                    //Создадим IndexSearcher
                    searcher = new IndexSearcher(reader);

                    //Если в строке поиска меньше символов чем нужно, то кидаем сообщение в UI 
                    if (queryString.Length < minSymbPerSearch)
                    {
                        //придумать коды ошибок, чтобы уже на UI возвращать ошибку в нужном языке langData.lbl_search_not_found_message[lang]
                        xsearchObjList.Add(new XSearchObjJSON("error001"));
                        //удаляем Searcher и Reader;
                        searcher.Dispose();
                        reader.Dispose();
                        return xsearchObjList;
                    }

                    // Строим Query объект                
                    //****************************************************************************************
                    // Внимание!!! добавляем "*" согласно синтаксу Lucene ищет вхождения по всему индексированномоу тексту
                    //****************************************************************************************
                    Regex lucenePattern = new Regex(LUCENE_ESCAPE_CHARS);

                    string escapedQuery = queryString;
                    //escapedQuery = lucenePattern.Replace(queryString, REPLACEMENT_STRING);
                    //string escapedQuery1 = Regex.Replace(escapedQuery, @"\\", @"\");
                    //escapedQuery = Helper.RemoveDuplicateCharsFast(escapedQuery, '\\');
                    

                    QueryParser parser = new QueryParser(Version.LUCENE_30, SearchField, analyzer);

                    //BooleanQuery q = new BooleanQuery();



                    //разершаем поиск вида "*кредит"                   
                    parser.AllowLeadingWildcard = true;

                    /*устанавливаем поиск вхождения по всем термам term1 AND term2 AND... QueryParser.AND_OPERATOR
                     * (исправляет кейс ниже)
                    Шаги воспроизведения:                    
                    1.	Ввести «моя креди» и нажать на кнопку Далее
                    Актуальный результат:
                    Найдена только услуга «Моя кредитная история»
                    Ожидаемый результат:
                    Найдены услуги имеющие либо слово моя либо слово креди
                    */

                    parser.DefaultOperator = QueryParser.AND_OPERATOR;

                    /* но если требуется вхождение по "инн 1" всех "инн 102, инн 101233"
                     * то придется убрать либо ставить QueryParser.OR_OPERATOR
                     */

                    //parser.DefaultOperator = QueryParser.OR_OPERATOR;

                    //Query query = parser.Parse(QueryParser.Escape(queryString)+"*"); 
                    Query query = parser.Parse(escapedQuery + '*');

                    
                    //Query query = parser.Parse(queryString);


                    // Search for the query
                    TopScoreDocCollector collector = TopScoreDocCollector.Create(maxHitsPerSearch, false);
                    searcher.Search(query, collector);

                    ScoreDoc[] hits = collector.TopDocs().ScoreDocs;

                    int hitCount = hits.Length;                               


                    if (hitCount == 0)
                    {                        
                        //Console.WriteLine("Совпадений для \"" + queryString + "\" не найдено");
                        //пример сообщения для ui
                        //xsearchObjList.Add(new XSearchObjJSON("Услуги не найдены"));
                    }
                    else
                    {
                        //Console.WriteLine("Совпадений для \"" + queryString + "\" найдено:");

                        // Ищем по всем документам в индексе
                        for (int i = 0; i < hitCount; i++)
                        {
                            ScoreDoc scoreDoc = hits[i];
                            int docId = scoreDoc.Doc;
                            //float docScore = scoreDoc.Score;

                            Document doc = searcher.Doc(docId);

                            //вытаскиваем все поля и создаем объект для сериализации

                            string logo = (doc.Get(Helper.GETNAME(new { read_xsobj_field.logo })) == String.Empty || doc.Get(Helper.GETNAME(new { read_xsobj_field.logo })) == null) ?
                                String.Empty : doc.Get(Helper.GETNAME(new { read_xsobj_field.logo }));

                            if (!Path.HasExtension(logo))

                                logo = logo + imagesExtention;

                            XSearchObjJSON temp = new XSearchObjJSON(doc.Get(Helper.GETNAME(new { read_xsobj_field.id_service })),
                                doc.Get(Helper.GETNAME(new { read_xsobj_field.alias })),
                                doc.Get(Helper.GETNAME(new { read_xsobj_field.scen_alias })), doc.Get(Helper.GETNAME(new { read_xsobj_field.name_service })),
                                logo);

                            //ничего не пишем в сообщения
                            temp.message = string.Empty;



                            //добавляем объекты удачного поиска для сериализации
                            xsearchObjList.Add(temp);
                        }                   
                    }

                    //удаляем Searcher и Reader;
                    searcher.Dispose();
                    reader.Dispose();

                    //return json;
                    return xsearchObjList;

                }
                catch (Exception ex)
                {
                    if (ex is ParseException || ex is IOException || ex is NullReferenceException)
                    {
                        //удаляем Searcher и Reader???
                        if (searcher != null) searcher.Dispose();
                        reader.Dispose();
                        //сообщаем об ошибках наружу
                    }
                    throw;
                }
            }
        }

        void Optimize(ref IndexWriter writer)
        {
            try
            {
                if (writer != null)
                    writer.Optimize();
            }
            catch (Exception ex)
            {
                throw new Exception("Error Writer optimizing! \n" + ex.Message);
            }
        }

        void Dispose(ref IndexWriter writer)
        {
            try
            {
                if (writer != null)
                //проверяем что тип Writer в памяти, а не в каталоге
                //if (writer.Directory.GetType().Equals(memWriter.Directory.GetType()))
                {
                    writer.Dispose();
                }

            }
            catch (Exception ex)
            {
                if (ex is NullReferenceException)
                {
                    //Console.WriteLine("Error Writer optimizing! \n" + ex.Message);                    
                    //return;
                    throw new NullReferenceException("Error Writer optimizing! \n" + ex.Message);
                }
                if (ex is AlreadyClosedException)
                {
                    //Console.WriteLine("Writer already closed! \n" + ex.Message);
                    //throw;
                    //return;
                    throw new AlreadyClosedException("Writer already closed! \n" + ex.Message);
                }
                //throw;
            }
        }
    }


}
