using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;

namespace TrainBot.Commands
{
    public class CommandManager : ICommandManager
    {
        private readonly Func<CommandBase[]> m_GetCommands;
        private Dictionary<ECommands, CommandBase> m_CommandHandlers;

        public Dictionary<ECommands, CommandBase> CommandHandlers
        {
            get
            {
                if (m_CommandHandlers == null)
                {
                    m_CommandHandlers = new Dictionary<ECommands, CommandBase>(m_GetCommands()
                        .OrderBy(a => a.GetCommandTypeString())
                        .ToDictionary(a => a.GetCommandType(), a => a));
                }

                return m_CommandHandlers;
            }
        }

        public CommandBase GetCommandHandler(string command)
        {
            ECommands commandEnum = CommandBase.GetCommandType(command);
            return CommandHandlers[commandEnum];
        }

        public CommandManager(Func<CommandBase[]> getCommands)
        {
            m_GetCommands = getCommands;
        }
    }
}