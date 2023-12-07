using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;
using TrainBot.Root;

namespace TrainBot.Commands
{
    public class LearnCommand : NextCommand
    {
        public override string GetCommandIconUnicode()
        {
            throw new NotImplementedException();
        }

        public override string GetCommandTextDescription()
        {
            throw new NotImplementedException();
        }

        public override AnswerItem ProcessAnswer(AnswerItem previousAnswerItem, MessageItem mItem)
        {
            throw new NotImplementedException();
        }

        public override LearnUnit ProcessLearn(MessageItem mItem)
        {
            throw new NotImplementedException();
        }

        public override AnswerItem ProcessNext(AnswerItem previousAnswerItem, LearnUnit lUnit)
        {
            throw new NotImplementedException();
        }

        public override ECommands GetCommandType()
        {
            return ECommands.LEARN;
        }
    }
}