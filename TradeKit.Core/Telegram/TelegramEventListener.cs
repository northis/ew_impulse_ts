using TL;
using WTelegram;

namespace TradeKit.Core.Telegram
{
    public class TelegramEventListener
    {
        private readonly string m_ApiId;
        private readonly string m_ApiHash;
        private readonly string m_Phone;

        public TelegramEventListener(string apiId, string apiHash, string phone)
        {
            m_ApiId = apiId;
            m_ApiHash = apiHash;
            m_Phone = phone;
        }

        public async Task Init()
        {
            await using var client = new Client();
            User myself = await client.LoginUserIfNeeded();
        }

        string Config(string what)
        {
            switch (what)
            {
                case "api_id": return m_ApiId;
                case "api_hash": return m_ApiHash;
                case "phone_number": return m_Phone;
                case "verification_code": Console.Write("Code: "); return Console.ReadLine();
                case "first_name": Console.Write("First Name: "); return Console.ReadLine();
                case "last_name": Console.Write("Last Name: "); return Console.ReadLine();
                case "password": return GetPassword();
                default: return null;
            }
        }

        private string GetPassword()
        {
            var pass = string.Empty;
            ConsoleKey key;
            do
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
                key = keyInfo.Key;

                if (key == ConsoleKey.Backspace && pass.Length > 0)
                {
                    Console.Write("\b \b");
                    pass = pass[0..^1];
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    Console.Write("*");
                    pass += keyInfo.KeyChar;
                }
            } while (key != ConsoleKey.Enter);

            return pass;
        }
    }
}
