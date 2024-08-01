using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TradeKit.Core.Common;
using TrainBot.Commands;
using TrainBot.Commands.Common;

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
            try
            {
                await OnMessage(callbackQuery.Message!, callbackQuery.Data!, callbackQuery.From);
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(CallbackQuery)}: {ex}");
            }
        }

        public async Task InlineQuery(InlineQuery inlineQuery)
        {
            var userId = inlineQuery.From.Id;
            var q = inlineQuery.Query;
            // TODO for use in future
        }

        public async Task OnMessage(Message msg)
        {
            try
            {
                await OnMessage(msg, msg.Text!, msg.From!);
            }
            catch (Exception ex)
            {
                Logger.Write($"{nameof(OnMessage)}: {ex}");
            }
        }

        public void OnReceiveError(ApiRequestException e)
        {
            Logger.Write($"{nameof(OnReceiveError)}: {e.Message}");
        }

        public void OnReceiveGeneralError(Exception e)
        {
            Logger.Write($"{nameof(OnReceiveGeneralError)}: {e.Message}");
        }

        private async Task SendPhotos(string[] pathImages, 
            Func<InputMediaPhoto[], Task> actionToSend)
        {
            int length = pathImages.Length;
            var streamReaders = new StreamReader[length];
            var photos = new InputMediaPhoto[length];
            try
            {
                for (int i = 0; i < length; i++)
                {
                    string path = pathImages[i];
                    var reader = new StreamReader(path);
                    streamReaders[i] = reader;
                    var stream = new InputFileStream(
                        reader.BaseStream, Path.GetFileName(path));
                    var photo = new InputMediaPhoto(stream);
                    photos[i] = photo;
                }

                await actionToSend(photos);
            }
            finally
            {
                foreach (StreamReader streamReader in streamReaders)
                {
                    streamReader.Close();
                }
            }
        }

        private async Task HandleCommand(MessageItem mItem)
        {
            var handler = m_CommandManager.GetCommandHandler(mItem.Command);
            AnswerItem reply = handler.Reply(mItem);

            reply.Markup ??= new ReplyKeyboardRemove();

            bool useMainImage = !string.IsNullOrEmpty(reply.PathMainImage);
            bool useExtraImages = reply.PathImages is { Length: > 0 };
            bool useImages = useMainImage || useExtraImages;

            if (useImages)
            {
                if (useExtraImages)
                {
                    await SendPhotos(reply.PathImages
                            .Where(a => a != reply.PathMainImage)
                            .ToArray(),
                        a => m_Client.SendMediaGroupAsync(mItem.ChatId, a));
                }

                if (useMainImage)
                {
                    await SendPhotos(new[] {reply.PathMainImage},
                        a => m_Client.SendPhotoAsync(mItem.ChatId,
                            a[0].Media,
                            replyMarkup: reply.Markup,
                            caption: reply.Message));
                    return;
                }
            }

            await m_Client.SendTextMessageAsync(
                mItem.ChatId, reply.Message, replyMarkup: reply.Markup);
        }

        private async Task OnMessage(Message msg, string argumentCommand, User user)
        {
            // TODO add user to a store

            string? commandOnly = null;
            string? textOnly = null;
            if (!string.IsNullOrEmpty(argumentCommand))
            {
                if (argumentCommand.StartsWith(CommandBase.COMMAND_START_CHAR))
                {
                    var sp = argumentCommand.Split(CommandBase.COMMAND_SEPARATOR_CHAR,
                        StringSplitOptions.RemoveEmptyEntries);
                    if (sp.Length > 0)
                    {
                        commandOnly = sp[0];
                    }

                    if (sp.Length > 1)
                    {
                        textOnly = sp[1];
                    }
                }
            }

            if (commandOnly == null)
            {
                MessageEntity? firstEntity = msg.Entities?.FirstOrDefault();
                if (firstEntity == null)
                {
                    Logger.Write($"No command found: {msg.Text}");
                    return;
                }

                commandOnly = msg.Text?.Substring(firstEntity.Offset, firstEntity.Length);

                var normalString = NormalizeString(msg.Text!);
                textOnly = normalString.Replace(commandOnly!, string.Empty);
                if (string.IsNullOrEmpty(commandOnly))
                {
                    Logger.Write($"Null command from text: {msg.Text}");
                    return;
                }
            }

            await HandleCommand(new MessageItem
            {
                Command = commandOnly,
                ChatId = msg.Chat.Id,
                UserId = user.Id,
                Text = msg.Text!,
                TextOnly = textOnly!
            });
        }
        
        public static string NormalizeString(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;

            var normalStr = new StringBuilder();

            foreach (var chr in str)
            {
                var cat = char.GetUnicodeCategory(chr);

                if (cat != UnicodeCategory.OtherSymbol && cat != UnicodeCategory.Surrogate &&
                    cat != UnicodeCategory.NonSpacingMark)
                    normalStr.Append(chr);
            }

            return normalStr.ToString();
        }
    }
}