public class InputCommandInternalMessageBase
{
	public InputCommand Command;

	public bool IsDirection()
	{
		if ((uint)(Command - 7) <= 3u)
		{
			return true;
		}
		return false;
	}
}
