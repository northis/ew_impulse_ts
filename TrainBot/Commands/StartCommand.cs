using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;

namespace TrainBot.Commands
{
    public class StartCommand : HelpCommand
    {
        public StartCommand(Func<CommandBase[]> getAllCommands) : base(getAllCommands)
        {
        }

        public override string GetCommandIconUnicode()
        {
            return "🖐";
        }

        public override string GetCommandTextDescription()
        {
            return "Welcome";
        }

        public override ECommands GetCommandType()
        {
            return ECommands.START;
        }

        public override string GetHelpMessage()
        {
            return
                $"Pattern recognition and AI-train bot{Environment.NewLine}{Environment.NewLine}List of available commands:{Environment.NewLine}{base.GetHelpMessage()}";
        }
    }
}