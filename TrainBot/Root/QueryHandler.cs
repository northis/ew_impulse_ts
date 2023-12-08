using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using TradeKit.Core;
using TrainBot.Commands;

namespace TrainBot.Root
{
    public class QueryHandler
    {
        private readonly TelegramBotClient m_Client;
        private readonly ICommandManager m_CommandManager;
        
        public QueryHandler(TelegramBotClient client, ICommandManager commandManager)
        {
            m_Client = client;
            m_CommandManager = commandManager;
        }

        public async Task CallbackQuery(CallbackQuery callbackQuery)
        {
            var userId = callbackQuery.From.Id;
        }

        public static string GetNoEmojiString(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            var noEmojiStr = new StringBuilder();

            foreach (var chr in str)
            {
                var cat = char.GetUnicodeCategory(chr);

                if (cat != UnicodeCategory.OtherSymbol && cat != UnicodeCategory.Surrogate &&
                    cat != UnicodeCategory.NonSpacingMark)
                    noEmojiStr.Append(chr);
            }

            return noEmojiStr.ToString();
        }

        public async Task InlineQuery(InlineQuery inlineQuery)
        {
            var userId = inlineQuery.From.Id;
            var q = inlineQuery.Query;
        }

        public async Task OnMessage(Message msg)
        {
        }

        public void OnReceiveError(ApiRequestException e)
        {
            Logger.Write($"{nameof(OnReceiveError)}: {e.Message}");
        }

        public void OnReceiveGeneralError(Exception e)
        {
            Logger.Write($"{nameof(OnReceiveGeneralError)}: {e.Message}");
        }

        private async Task HandleArgumentCommand(Message msg, string argumentCommand, long userId)
        {
        }

        private async Task HandleCommand(MessageItem mItem)
        {
            var handler = m_CommandManager.GetCommandHandler(mItem.Command);
            var reply = handler.Reply(mItem);
        }

        private async Task OnMessage(Message msg, string argumentCommand, User user)
        {
        }
    }
}