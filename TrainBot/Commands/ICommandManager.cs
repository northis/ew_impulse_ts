using TrainBot.Commands.Common;
using TrainBot.Commands.Enums;

namespace TrainBot.Commands
{
    public interface ICommandManager
    {
        Dictionary<ECommands, CommandBase> CommandHandlers { get; }

        CommandBase GetCommandHandler(string command);
    }
}