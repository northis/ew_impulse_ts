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
        
        public IReplyMarkup GetLearnMarkup()
        {
            var mkp = new InlineKeyboardMarkup(new[]
            {
                new InlineKeyboardButton("✅") {CallbackData = POSITIVE},
                new InlineKeyboardButton("✅ (flat)") {CallbackData = POSITIVE_FLAT},
                new InlineKeyboardButton("❌") {CallbackData = NEGATIVE},
                new InlineKeyboardButton("💔") {CallbackData = BROKEN}
            });

            return mkp;
        }

        public override AnswerItem Reply(MessageItem mItem)
        {
            var answerItem = new AnswerItem
            {
                Message = GetCommandIconUnicode(),
                Markup = GetLearnMarkup(),
            };

            try
            {
                if (string.IsNullOrWhiteSpace(mItem.TextOnly))
                {
                    FolderItem? folder = m_FolderManager.GetFolder(mItem.UserId);
                    if (folder == null || folder.PathImages.Length == 0)
                    {
                        answerItem.Message =
                            $"No data to train. Try again: {COMMAND_START_CHAR}{GetCommandType().ToString().ToLowerInvariant()}";
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
                    answerItem.Message = sb.ToString();
                }
                else
                {
                    if (!m_ActionMapper.TryGetValue(
                            mItem.TextOnly, out Action<long>? action))
                    {
                        answerItem.Message = "Unknown command";
                        answerItem.Markup = null;
                        return answerItem;
                    }

                    action(mItem.UserId);
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