using MsgPack;

namespace Messages;

public struct GetRecipients
{
	public const uint TypeCode = 4012u;

	public string ConversationId;

	public static void Pack(Packer packer, GetRecipients val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(4012u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		if (val.ConversationId == null)
		{
			packer.PackString(string.Empty);
		}
		else
		{
			packer.PackString(val.ConversationId);
		}
	}

	public static GetRecipients Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new GetRecipients
		{
			ConversationId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<GetRecipients ConversationId=" + ConversationId + ">";
	}
}
