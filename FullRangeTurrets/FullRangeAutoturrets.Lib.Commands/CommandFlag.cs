using System;

namespace FullRangeAutoturrets.Lib.Commands;

[Flags]
public enum CommandFlag
{
	None = 0,
	Admin = 1,
	BlockExecution = 2,
	IncludePrefix = 3
}
