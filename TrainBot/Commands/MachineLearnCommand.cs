using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;
using TrainBot.FoldersLogic;
using TrainBot.Root;

namespace TrainBot.Commands
{
    public class MachineLearnCommand : CommandBase
    {
        private readonly FolderManager m_FolderManager;
        private readonly BotSettingHolder m_Settings;

        public MachineLearnCommand(
            FolderManager folderManager, BotSettingHolder settings)
        {
            m_FolderManager = folderManager;
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

            bool res = m_FolderManager.CleanDirs();
            return new AnswerItem
            {
                Message = res 
                    ? "Folders cleaned" 
                    : "Something went wrong, check log"
            };
        }
    }
}