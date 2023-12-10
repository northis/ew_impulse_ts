using Plotly.NET.TraceObjects;
using System.Globalization;
using System.Text;
using Telegram.Bot.Types.ReplyMarkups;
using TradeKit.Core;
using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;
using TrainBot.FoldersLogic;
using TrainBot.Root;

namespace TrainBot.Commands
{
    public class LearnCommand : CommandBase
    {
        private readonly FolderManager m_FolderManager;
        private readonly Dictionary<string, Action<long>> m_ActionMapper;

        private const string POSITIVE = "p";
        private const string POSITIVE_FLAT = "pf";
        private const string NEGATIVE = "n";
        private const string BROKEN = "b";

        private readonly string m_Command;
        private readonly IReplyMarkup m_ReplyMarkup;

        public LearnCommand(FolderManager folderManager)
        {
            m_FolderManager = folderManager;
            m_ActionMapper = new Dictionary<string, Action<long>>
            {
                {
                    POSITIVE, m_FolderManager.MovePositiveFolder
                },
                {
                    POSITIVE_FLAT, m_FolderManager.MovePositiveFlatFolder
                },
                {
                    NEGATIVE, m_FolderManager.MoveNegativeFolder
                },
                {
                    BROKEN, m_FolderManager.MoveBrokenFolder
                }
            };

            m_Command = $"{COMMAND_START_CHAR}{ECommands.LEARN.ToString().ToLowerInvariant()}";

            m_ReplyMarkup = new InlineKeyboardMarkup(new[]
            {
                new InlineKeyboardButton("✅") {CallbackData = $"{m_Command} {POSITIVE}"},
                new InlineKeyboardButton("✅ (flat)") {CallbackData = $"{m_Command} {POSITIVE_FLAT}"},
                new InlineKeyboardButton("❌") {CallbackData = $"{m_Command} {NEGATIVE}"},
                new InlineKeyboardButton("💔") {CallbackData = $"{m_Command} {BROKEN}"}
            });
        }

        public override string GetCommandIconUnicode()
        {
            return "🎓";
        }

        public override string GetCommandTextDescription()
        {
            return "Train AI model.";
        }

        public override ECommands GetCommandType()
        {
            return ECommands.LEARN;
        }

        public AnswerItem ReplyInner(AnswerItem answerItem, MessageItem mItem)
        {
            FolderItem? folder = m_FolderManager.GetFolder(mItem.UserId);
            if (folder == null || folder.PathImages.Length == 0)
            {
                answerItem.Message =
                    $"No data to train. Try again: {m_Command}";
                answerItem.Markup = null;
                return answerItem;

            }

            answerItem.PathImages = folder.PathImages;
            answerItem.PathMainImage = folder.PathImages[0];

            JsonSymbolStatExport sData = folder.SymbolStatData;
            var sb = new StringBuilder();

            bool isBuy = sData.Take > sData.Stop;
            string tradeType = isBuy ? "buy" : "sell";

            sb.AppendLine(
                $"#{sData.Symbol} {tradeType} {sData.Entry.ToString($"F{sData.Accuracy}", CultureInfo.InvariantCulture)}");

            sb.AppendLine(sData.Result ? "TP hit" : "SL hit");
            sb.AppendLine($"Setups to train: {folder.FoldersCount}");
            answerItem.Message = sb.ToString();
            answerItem.Markup = m_ReplyMarkup;
            return answerItem;
        }


        public override AnswerItem Reply(MessageItem mItem)
        {
            var answerItem = new AnswerItem
            {
                Message = $"Something went wrong. Try again: {m_Command}",
                Markup = null,
            };

            try
            {
                if (string.IsNullOrWhiteSpace(mItem.TextOnly))
                {
                    return ReplyInner(answerItem, mItem);
                }
                else
                {
                    if (!m_ActionMapper.TryGetValue(
                            mItem.TextOnly, out Action<long>? action))
                    {
                        answerItem.Message = "Unknown command";
                        return answerItem;
                    }

                    action(mItem.UserId);
                    return ReplyInner(answerItem, mItem);
                }

            }
            catch (Exception ex)
            {
                answerItem.Message += Environment.NewLine + ex.Message;
            }

            return answerItem;
        }
    }
}