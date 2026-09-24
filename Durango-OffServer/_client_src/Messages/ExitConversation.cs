using MsgPack;

namespace Messages;

public struct ExitConversation
{
	public const uint TypeCode = 4010u;

	public string ConversationId;

	public static void Pack(Packer packer, ExitConversation val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(4010u);
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

	public static ExitConversation Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new ExitConversation
		{
			ConversationId = unpacker.LastReadData.AsString()
		};
	}

	public override string ToString()
	{
		return "<ExitConversation ConversationId=" + ConversationId + ">";
	}
}
