using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
//using eKassir.Terminal.XSearch;

namespace SearchEngine
{    
    //TODO: постараться сделать в виде обертки над любым объектом, парсить поля и т.п.
    //объект для поиска (услуга)
    //[StructLayout(LayoutKind.Sequential, Pack = 1)]
    //public struct XSearchObj
    public class XSearchObj
    {        
        //здесь можно добавить поля из базы которые хотим проиндексировать
        //(желательно имена тоже выбирать по назвниям полей таблиц)             

        public char[] id_service { get; set; }      //id услуги на терминале
        public char[] alias { get; set; }           //псевдоним услуги
        public char[] scen_alias { get; set; }      //псевдоним сценария
        public char[] name_service { get; set; }    //Display Name имя услуги (текст, который индексируем)
        public char[] logo { get; set; }            //имя файла логотипа услуги без расширения
        public char[] version { get; set; }         //версия услуги (для сравнения изменений)        
        public char[] search_tag { get; set; }      //поле для всех ключевых слов (будет для всех если не делать динамического добавления атрибутов)  
        //public char[] message { get; set; }         //поле для всех сообщений

        public XSearchObj() { }

        //конструктор который берет данные из сеттинга
        public XSearchObj(string Alias, string Name)// : this()
        {
            alias = Alias.ToCharArray();
            scen_alias = Name.ToCharArray();            
        }

        //конструктор который берет только данные из базы
        public XSearchObj(string Idservice, string Alias, string ScenAlias, string NameService, string Logo, string Version, string SearchTag)// : this()
        {
            id_service = Idservice.ToCharArray();
            alias = Alias.ToCharArray();
            scen_alias = ScenAlias.ToCharArray();
            name_service = NameService.ToCharArray();
            logo = Logo.ToCharArray();
            version = Version.ToCharArray();
            search_tag = SearchTag.ToCharArray();
            //имена и они же будущие переменные (выбранные по именам из таблиц)
            //Expression<Func<string>>[] exprArr = { () => alias, () => name_service }; 
        }

        //всё обнулить
        public void NullAll() {
            this.scen_alias = null;
            this.id_service = null;
            this.alias = null;
            this.name_service = null;
            this.version = null;
            this.logo = null;
            this.search_tag = null;
            //this.message = null;
        }

        public IEnumerable<string> GetStringsfromAll()
        {
            List<string> temp = new List<string>();
            try
            {
                temp.Add(id_service.ToString());
                temp.Add(alias.ToString());
                temp.Add(scen_alias.ToString());
                temp.Add(name_service.ToString());
                temp.Add(logo.ToString());
                temp.Add(version.ToString());
                temp.Add(search_tag.ToString());
            }
            catch (NullReferenceException ex)
            {
                //Console.WriteLine("One of the field is null: " + ex.Message);
                throw new NullReferenceException("One of the field is null! \n" + ex.Message);
                //throw;
            }
            catch (Exception ex)
            {
                throw new Exception("Fatal SearchEngine error! \n" + ex.Message);
            }

            return temp;
        }

    }    

    //вспомогательный класс для возврата в UI найденной услуги
    [DataContract]
    public class XSearchObjJSON
    {
        public XSearchObjJSON(XSearchObj obj)
        {
            id = obj.id_service.ToString();
            alias = obj.alias.ToString();
            name = obj.scen_alias.ToString();
            DisplayName = obj.name_service.ToString();
            logo = obj.logo.ToString();
            //message = obj.message.ToString();
        }

        public XSearchObjJSON(string id, string alias, string scen_alias, string name_service, string logo)
        {
            this.id = id;
            this.alias = alias;
            this.name = scen_alias;
            this.DisplayName = name_service;
            this.logo = logo;
        }

        public XSearchObjJSON(string message)
        {
            this.message = message;
        }

        //по DataMember (не сериализуется из JavaScriptSerializer)
        [DataMember(Name = "id")]
        public string id { get; set; }             //id услуги в базе терминала
        [DataMember(Name = "alias")]
        public string alias { get; set; }          //псевдоним услуги
        //[DataMember(Name = "name_service")]
        [DataMember(Name = "DisplayName")]
        public string DisplayName { get; set; }    //Display Name имя услуги (текст, который индексируем)
        [DataMember(Name = "logo")]
        public string logo { get; set; }           //имя файла логотипа услуги без расширения
        //[DataMember(Name = "scen_alias")]
        [DataMember(Name = "name")]
        public string name { get; set; }           //псевдоним сценария  
        [DataMember(Name = "message")]
        public string message { get; set; }           //псевдоним сообщения для вывода в UI        
    }
}
