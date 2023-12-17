using TradeKit.Core;
using TradeKit.ML;
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
            
            IEnumerable<LearnFilesItem> filesToLearnPositive = Directory
                .EnumerateDirectories(m_Settings.PositiveFolder)
                .Select(a => LearnFilesItem.FromDirPath(true, a));
            IEnumerable<LearnFilesItem> filesToLearnNegative = Directory
                .EnumerateDirectories(m_Settings.NegativeFolder)
                .Select(a => LearnFilesItem.FromDirPath(false, a));

            IEnumerable<LearnFilesItem> dataToLearn = 
                filesToLearnPositive.Concat(filesToLearnNegative);

            TradeKit.ML.MachineLearning.RunLearn(
                dataToLearn, m_Settings.MlModelPath);

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