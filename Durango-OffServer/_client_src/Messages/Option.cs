using MsgPack;

namespace Messages;

public struct Option
{
	public string Key;

	public static void Pack(Packer packer, Option val, bool hint = false)
	{
		packer.PackArrayHeader(1);
		if (val.Key == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.Key);
		}
	}

	public static Option Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new Option
		{
			Key = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<Option Key=" + Key + ">";
	}
}
