using System.Text.RegularExpressions;

namespace CloudGame.Auth
{
    /// <summary>
    /// Validador local que replica las reglas de Unity Authentication (UGS):
    /// Usuario: 3-20 caracteres, permitidos a-z, 0-9 y . - @ _ (insensible a mayúsculas).
    /// Contraseña: 8-30 caracteres, con al menos 1 mayúscula, 1 minúscula, 1 número y 1 símbolo.
    /// </summary>
    public static class CredentialValidator
    {
        private static readonly Regex UsernameRegex = new Regex("^[a-zA-Z0-9.\\-_@]{3,20}$", RegexOptions.Compiled);
        private static readonly Regex HasUppercase = new Regex("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex HasLowercase = new Regex("[a-z]", RegexOptions.Compiled);
        private static readonly Regex HasDigit = new Regex("[0-9]", RegexOptions.Compiled);
        private static readonly Regex HasSymbol = new Regex("[^a-zA-Z0-9]", RegexOptions.Compiled);

        public static bool IsUsernameValid(string username, out string error)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                error = "El usuario es obligatorio.";
                return false;
            }

            username = username.Trim();
            if (username.Length < 3 || username.Length > 20)
            {
                error = "El usuario debe tener entre 3 y 20 caracteres.";
                return false;
            }

            if (!UsernameRegex.IsMatch(username))
            {
                error = "El usuario solo puede contener letras, n\u00fameros y los s\u00edmbolos . - @ _";
                return false;
            }

            error = null;
            return true;
        }

        public static bool IsPasswordValid(string password, out string error)
        {
            if (string.IsNullOrEmpty(password))
            {
                error = "La contrase\u00f1a es obligatoria.";
                return false;
            }

            if (password.Length < 8 || password.Length > 30)
            {
                error = "La contrase\u00f1a debe tener entre 8 y 30 caracteres.";
                return false;
            }

            var missing = new System.Collections.Generic.List<string> { };
            if (!HasUppercase.IsMatch(password)) missing.Add("1 may\u00fascula");
            if (!HasLowercase.IsMatch(password)) missing.Add("1 min\u00fascula");
            if (!HasDigit.IsMatch(password)) missing.Add("1 n\u00famero");
            if (!HasSymbol.IsMatch(password)) missing.Add("1 s\u00edmbolo (!?@#$%&)");

            if (missing.Count > 0)
            {
                error = "La contrase\u00f1a debe incluir al menos " + string.Join(", ", missing) + ".";
                return false;
            }

            error = null;
            return true;
        }

        public static bool IsValid(string username, string password, out string error)
        {
            if (!IsUsernameValid(username, out error))
            {
                return false;
            }

            if (!IsPasswordValid(password, out error))
            {
                return false;
            }

            error = null;
            return true;
        }
    }
}