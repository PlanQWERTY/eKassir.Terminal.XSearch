using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lucene.Net.Analysis;

namespace SearchEngine
{
    public class CaseInsensitiveWhitespaceAnalyzer : Analyzer
    {
        //Java code
        //@Override
        /*protected TokenStreamComponents createComponents(String arg0, Reader arg1) {
            Tokenizer tokenizer = new WhitespaceTokenizer(arg1);
            TokenStream filter = new LowerCaseFilter(tokenizer);
            return new TokenStreamComponents(tokenizer, filter);
    }*/

        /// <summary>
        /// </summary>
        public override TokenStream TokenStream(string fieldName, TextReader reader)
        {
            TokenStream t = null;
            t = new WhitespaceTokenizer(reader);
            t = new LowerCaseFilter(t);            
            return t;
        }
    }
}
