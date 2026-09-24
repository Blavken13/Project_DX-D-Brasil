using MsgPack;

namespace Messages;

public struct NotificationCanceled
{
	public const uint TypeCode = 3716u;

	public string Id;

	public static void Pack(Packer packer, NotificationCanceled val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(3716u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.Id == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.Id);
		}
	}

	public static NotificationCanceled Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new NotificationCanceled
		{
			Id = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<NotificationCanceled Id=" + Id + ">";
	}
}
