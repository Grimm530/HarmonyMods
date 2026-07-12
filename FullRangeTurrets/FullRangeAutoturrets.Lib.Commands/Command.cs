namespace FullRangeAutoturrets.Lib.Commands;

public class Command
{
	public string Name;

	public CommandType Type;

	public CommandFlag Flags;

	private event CommandHandlerAction _action;

	public Command(string name, CommandType type, CommandHandlerAction action, CommandFlag flags)
	{
		Name = name;
		Type = type;
		Flags = flags;
		AddListener(action);
	}

	public bool HasListeners()
	{
		return this._action != null;
	}

	public void AddListener(CommandHandlerAction action)
	{
		_action += action;
	}

	public void RemoveListener(CommandHandlerAction action)
	{
		_action -= action;
	}

	public void Execute(object sender, params object[] args)
	{
		this._action?.Invoke(sender, args);
	}
}
