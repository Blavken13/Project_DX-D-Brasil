using MsgPack;

namespace Messages;

public struct Unblock
{
	public const uint TypeCode = 4017u;

	public string EntityId;

	public static void Pack(Packer packer, Unblock val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(4017u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.EntityId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.EntityId);
		}
	}

	public static Unblock Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new Unblock
		{
			EntityId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<Unblock EntityId=" + EntityId + ">";
	}
}
