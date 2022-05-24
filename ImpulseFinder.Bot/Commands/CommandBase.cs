using ImpulseFinder.Bot.Dto;

namespace ImpulseFinder.Bot.Commands
{
    public abstract class CommandBase
    {
        public const string COMMAND_START_CHAR = "/";

        public string GetCommandDescription()
        {
            return GetCommandIconUnicode() + GetCommandTextDescription();
        }

        public abstract string GetCommandIconUnicode();
        public abstract string GetCommandTextDescription();
        public abstract ECommands GetCommandType();

        public virtual string GetCommandTypeString()
        {
            return GetCommandType().ToString();
        }

        public static ECommands GetCommandType(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentNullException(nameof(command), "A command cannot be empty");

            if (!command.StartsWith(COMMAND_START_CHAR))
                throw new ArgumentException($"A command must starts with '{COMMAND_START_CHAR}'", nameof(command));

            var cleanedCommand = command.Substring(1, command.Length - 1);

            if (!Enum.TryParse(cleanedCommand, true, out ECommands commandEnum))
                throw new NotSupportedException($"Command '{nameof(command)}' is not supported");

            return commandEnum;
        }

        public string GetFormattedDescription()
        {
            return $"{COMMAND_START_CHAR}{GetCommandType()} - {GetCommandDescription()}";
        }

        public abstract AnswerItem Reply(MessageItem mItem);
    }
}