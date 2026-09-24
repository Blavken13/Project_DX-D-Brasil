using MsgPack;

namespace Messages;

public struct TutorialEvent
{
	public const uint TypeCode = 701u;

	public string Event;

	public static void Pack(Packer packer, TutorialEvent val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(701u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.Event == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.Event);
		}
	}

	public static TutorialEvent Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new TutorialEvent
		{
			Event = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<TutorialEvent Event=" + Event + ">";
	}
}
