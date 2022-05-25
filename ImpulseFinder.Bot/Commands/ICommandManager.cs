namespace ImpulseFinder.Bot.Commands
{
    public interface ICommandManager
    {
        Dictionary<ECommands, CommandBase> CommandHandlers { get; }

        CommandBase GetCommandHandler(string command);
    }
}