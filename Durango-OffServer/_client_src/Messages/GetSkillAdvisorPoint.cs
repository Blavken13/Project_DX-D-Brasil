using MsgPack;

namespace Messages;

public struct GetSkillAdvisorPoint
{
	public const uint TypeCode = 3903u;

	public Skill Skill;

	public static void Pack(Packer packer, GetSkillAdvisorPoint val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(2);
			packer.Pack(3903u);
		}
		else
		{
			packer.PackArrayHeader(1);
		}
		Skill.Pack(packer, val.Skill);
	}

	public static GetSkillAdvisorPoint Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		return new GetSkillAdvisorPoint
		{
			Skill = Skill.Unpack(unpacker)
		};
	}

	public override string ToString()
	{
		return $"<GetSkillAdvisorPoint Skill={Skill}>";
	}
}
