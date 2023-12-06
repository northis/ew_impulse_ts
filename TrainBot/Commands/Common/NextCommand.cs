using Telegram.Bot.Types.ReplyMarkups;
using TrainBot.Root;

namespace TrainBot.Commands.Common
{
    public abstract class NextCommand : CommandBase
    {
        public const string NEXT_CMD = "next";

        public abstract override string GetCommandIconUnicode();

        public abstract override string GetCommandTextDescription();

        public IReplyMarkup GetLearnMarkup(long idCurrentWord)
        {
            var mkp = new InlineKeyboardMarkup(new[]
            {
                new InlineKeyboardButton("➡️Next") {CallbackData = NEXT_CMD}
            });

            return mkp;
        }

        public abstract AnswerItem ProcessAnswer(AnswerItem previousAnswerItem, MessageItem mItem);

        public abstract LearnUnit ProcessLearn(MessageItem mItem);
        public abstract AnswerItem ProcessNext(AnswerItem previousAnswerItem, LearnUnit lUnit);

        public override AnswerItem Reply(MessageItem mItem)
        {
            var answerItem = new AnswerItem
            {
                Message = GetCommandIconUnicode()
            };

            try
            {
                if (string.IsNullOrWhiteSpace(mItem.TextOnly) || NEXT_CMD == mItem.TextOnly)
                {
                    var learnUnit = ProcessLearn(mItem);
                    answerItem = ProcessNext(answerItem, learnUnit);
                }

                //else if (mItem.TextOnly.StartsWith(EditCommand.EditCmd) ||
                //         mItem.TextOnly.Contains(ImportCommand.SeparatorChar.ToString()))
                //{
                //    answerItem = EditCommand.Reply(mItem);
                //}
                //else
                //{
                //    answerItem = ProcessAnswer(answerItem, mItem);
                //}
            }
            catch (Exception ex)
            {
                answerItem.Message += Environment.NewLine + ex.Message;
            }

            return answerItem;
        }
    }
}