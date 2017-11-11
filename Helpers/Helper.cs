namespace eKassir.Terminal.XSearch.Helpers
{
    public static class Helper
    {
        //2 метода добавляющий обрамление (например кавычки для запроса в sql)
        public static string AddQuotes(string str)
        {
            return string.Format("\'{0}\'", str);
        }

        public static string[] AddQuotes(string[] str)
        {
            char[] charsToTrim = { '"', '\'' };

            for (int counter = 0; counter < str.Length; counter++)
            {
                str[counter] = AddQuotes(str[counter].Trim(charsToTrim));
            }
            return str;
        }

        //вытаскиваем имя переменной
        public static string GETNAME<T>(T myInput) where T : class
        {
            if (myInput == null)
                return string.Empty;

            return typeof(T).GetProperties()[0].Name;
        }

        //remove duplucate char from string
        public static string RemoveDuplicateCharsFast(string key, char ch)
        {
            // --- Removes duplicate chars using char arrays.
            int keyLength = key.Length;

            // Store encountered letters in this array.
            //char[] table = new char[keyLength];
            //int tableLength = 0;

            // Store the result in this array.
            char[] result = new char[keyLength];
            int resultLength = 0;

            // Loop through all characters
            foreach (char value in key)
            {
                // Scan the table to see if the letter is in it.
                bool exists = false;
                //for (int i = 0; i < tableLength; i++)
                //{
                    //if (value == table[i])
                    if (value == ch)
                    {
                        exists = true;
                        break;
                    }
                //}
                // If the letter is new, add to the table and the result.
                if (!exists)
                {
                    //table[tableLength] = value;
                    //tableLength++;

                    result[resultLength] = value;
                    resultLength++;
                }
            }
            // Return the string at this range.
            return new string(result, 0, resultLength);
        }
        
    }
}
