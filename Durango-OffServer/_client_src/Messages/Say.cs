using MsgPack;

namespace Messages;

public struct Say
{
	public Message_ Message;

	public static void Pack(Packer packer, Say val, bool hint = false)
	{
		packer.PackArrayHeader(1);
		Message_.Pack(packer, val.Message);
	}

	public static Say Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new Say
		{
			Message = Message_.Unpack(unpacker)
		};
	}

	public override string ToString()
	{
		return $"<Say Message={Message}>";
	}
}
