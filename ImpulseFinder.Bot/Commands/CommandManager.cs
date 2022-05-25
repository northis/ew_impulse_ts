namespace ImpulseFinder.Bot.Commands
{
    public class CommandManager : ICommandManager
    {
        private readonly Func<CommandBase[]> m_GetCommands;
        private readonly Dictionary<string, ECommands> m_HiddenCommandsMapping;
        private Dictionary<ECommands, CommandBase> m_CommandHandlers;

        public Dictionary<ECommands, CommandBase> CommandHandlers
        {
            get
            {
                return m_CommandHandlers 
                    ??= new Dictionary<ECommands, CommandBase>(m_GetCommands()
                    .OrderBy(a => a.GetCommandTypeString())
                    .ToDictionary(a => a.GetCommandType(), a => a));
            }
        }

        public CommandBase GetCommandHandler(string command)
        {
            if (!m_HiddenCommandsMapping.TryGetValue(command, out var commandEnum))
            {
                commandEnum = CommandBase.GetCommandType(command);
            }
            return CommandHandlers[commandEnum];
        }

        public CommandManager(
            Func<CommandBase[]> getCommands, 
            Dictionary<string, ECommands> hiddenCommandsMapping)
        {
            m_GetCommands = getCommands;
            m_HiddenCommandsMapping = hiddenCommandsMapping;
        }
    }
}