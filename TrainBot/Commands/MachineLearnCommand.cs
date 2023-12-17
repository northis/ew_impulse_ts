using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;
using TrainBot.MachineLearning;
using TrainBot.Root;

namespace TrainBot.Commands
{
    public class MachineLearnCommand : CommandBase
    {
        private readonly LearnManager m_LearnManager;
        private readonly BotSettingHolder m_Settings;

        public MachineLearnCommand(
            LearnManager learnManager, BotSettingHolder settings)
        {
            m_LearnManager = learnManager;
            m_Settings = settings;
        }

        public override string GetCommandIconUnicode()
        {
            return "ML";
        }

        public override string GetCommandTextDescription()
        {
            return "Run machine learning";
        }

        public override ECommands GetCommandType()
        {
            return ECommands.MACHINE_LEARN;
        }

        public override AnswerItem Reply(MessageItem mItem)
        {
            if (m_Settings.AdminUserId != mItem.UserId)
            {
                return new AnswerItem
                {
                    Message = "You cannot use this command."
                };
            }

            bool res = m_LearnManager.CheckOrRun();
            return new AnswerItem
            {
                Message = res 
                    ? "Learning started" 
                    : "Learning is already in progress..."
            };
        }
    }
}