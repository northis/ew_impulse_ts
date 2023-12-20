using Plotly.NET.TraceObjects;
using System.Globalization;
using System.Text;
using Telegram.Bot.Types.ReplyMarkups;
using TradeKit.Core;
using TradeKit.Json;
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
        
        private const string IMPULSE = "✅";
        private const string DIAGONAL = "↘";
        private const string NOT_AN_IMPULSE = "❌";
        private const string BROKEN_SETUP = "💔";

        private readonly string m_Command;

        public LearnCommand(FolderManager folderManager)
        {
            m_FolderManager = folderManager;
            m_ActionMapper = new Dictionary<string, Action<long>>
            {
                {
                    IMPULSE, m_FolderManager.MovePositiveFolder
                },
                {
                    DIAGONAL, m_FolderManager.MovePositiveFlatFolder
                },
                {
                    NOT_AN_IMPULSE, m_FolderManager.MoveNegativeFolder
                },
                {
                    BROKEN_SETUP, m_FolderManager.MoveBrokenFolder
                }
            };

            m_Command = $"{COMMAND_START_CHAR}{ECommands.LEARN.ToString().ToLowerInvariant()}";
        }

        public override string GetCommandIconUnicode()
        {
            return "🎓";
        }

        public override string GetCommandTextDescription()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Train AI model.");
            sb.AppendLine($"{IMPULSE} - Impulse");
            sb.AppendLine($"{DIAGONAL} - Diagonal");
            sb.AppendLine($"{NOT_AN_IMPULSE} - Not an impulse");
            sb.AppendLine($"{BROKEN_SETUP} - Broken setup");
            return sb.ToString();
        }

        public override ECommands GetCommandType()
        {
            return ECommands.LEARN;
        }

        public AnswerItem ReplyInner(AnswerItem answerItem, MessageItem mItem)
        {
            FolderStat? folder;
            do
            {
                folder = m_FolderManager.GetFolder(mItem.UserId);
                if (folder.InputFoldersCount == 0)
                {
                    break;
                }

            } while (folder.CurrentFolderPath ==null);

            if (folder.CurrentFolderPath == null)
            {
                answerItem.Message =
                    $"No data to train. Try again: {m_Command}";
                answerItem.Markup = null;
                return answerItem;
            }

            answerItem.PathImages = folder.PathImages
                .Where(a => a.EndsWith(Helper.MAIN_IMG_FILE_NAME_PNG))
                .ToArray();
            answerItem.PathMainImage = folder.PathImages.First(
                a => a.EndsWith(Helper.SAMPLE_IMG_FILE_NAME_PNG));

            JsonSymbolStatExport sData = folder.SymbolStatData;
            var sb = new StringBuilder();

            bool isBuy = sData.Take > sData.Stop;
            string tradeType = isBuy ? "buy" : "sell";

            sb.AppendLine(
                $"#{sData.Symbol} {tradeType} {sData.Entry.ToString($"F{sData.Accuracy}", CultureInfo.InvariantCulture)}");

            sb.AppendLine(sData.Result ? "TP hit" : "SL hit");
            sb.AppendLine($"Setups to train: {folder.InputFoldersCount}");
            answerItem.Message = sb.ToString();
            answerItem.Markup = new InlineKeyboardMarkup(new[]
            {
                new InlineKeyboardButton($"{IMPULSE} {folder.PositiveFoldersCount}") {CallbackData = $"{m_Command} {IMPULSE}"},
                new InlineKeyboardButton($"{DIAGONAL}  {folder.PositiveDiagonalFoldersCount}") {CallbackData = $"{m_Command} {DIAGONAL}" },
                new InlineKeyboardButton($"{NOT_AN_IMPULSE} {folder.NegativeFoldersCount}") {CallbackData = $"{m_Command} {NOT_AN_IMPULSE}"},
                new InlineKeyboardButton($"{BROKEN_SETUP} {folder.BrokenFoldersCount}") {CallbackData = $"{m_Command} {BROKEN_SETUP}"}
            });

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