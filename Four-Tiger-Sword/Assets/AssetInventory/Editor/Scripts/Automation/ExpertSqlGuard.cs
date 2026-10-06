namespace AssetInventory.Automation
{
    internal static class ExpertSqlGuard
    {
        internal static bool TryValidateSearchPhrase(string searchPhrase, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(searchPhrase) || searchPhrase[0] != '=') return true;

            string expression = searchPhrase.Substring(1);
            if (string.IsNullOrWhiteSpace(expression))
            {
                error = "Expert SQL requires a WHERE expression after '='.";
                return false;
            }

            bool singleQuoted = false;
            bool doubleQuoted = false;
            bool backtickQuoted = false;
            bool bracketQuoted = false;

            for (int i = 0; i < expression.Length; i++)
            {
                char current = expression[i];
                char next = i + 1 < expression.Length ? expression[i + 1] : '\0';

                if (singleQuoted)
                {
                    if (current == '\'' && next == '\'')
                    {
                        i++;
                    }
                    else if (current == '\'')
                    {
                        singleQuoted = false;
                    }
                    continue;
                }

                if (doubleQuoted)
                {
                    if (current == '"' && next == '"')
                    {
                        i++;
                    }
                    else if (current == '"')
                    {
                        doubleQuoted = false;
                    }
                    continue;
                }

                if (backtickQuoted)
                {
                    if (current == '`') backtickQuoted = false;
                    continue;
                }

                if (bracketQuoted)
                {
                    if (current == ']') bracketQuoted = false;
                    continue;
                }

                if (current == '\'')
                {
                    singleQuoted = true;
                    continue;
                }
                if (current == '"')
                {
                    doubleQuoted = true;
                    continue;
                }
                if (current == '`')
                {
                    backtickQuoted = true;
                    continue;
                }
                if (current == '[')
                {
                    bracketQuoted = true;
                    continue;
                }

                if (current == ';' || current == '\0' || current == '-' && next == '-' || current == '/' && next == '*' || current == '*' && next == '/')
                {
                    error = "Expert SQL must be one WHERE expression and cannot contain statement separators or SQL comments.";
                    return false;
                }
            }

            if (singleQuoted || doubleQuoted || backtickQuoted || bracketQuoted)
            {
                error = "Expert SQL contains an unterminated quoted value or identifier.";
                return false;
            }

            return true;
        }
    }
}
