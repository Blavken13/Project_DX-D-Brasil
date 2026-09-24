using System.Collections.Generic;
using Messages;
using Shared.Laboratory;
using Yaml;
using Yaml.Util;

public static class AvailablePersonalResearchExtension
{
	public static ResearchCategory GetCategory(this AvailablePersonalResearch msg)
	{
		foreach (Pair<string, int?> item in msg.ResearchableIds())
		{
			PersonalResearch personalResearch = SingletonDict<string, PersonalResearch>.Get(item.Item1);
			if (personalResearch != null)
			{
				return personalResearch.Category;
			}
		}
		return ResearchCategory.Invalid;
	}

	public static IEnumerable<Pair<string, int?>> ResearchableIds(this AvailablePersonalResearch msg)
	{
		if (msg.AvailableResearchIds != null)
		{
			string[] availableResearchIds = msg.AvailableResearchIds;
			string[] array = availableResearchIds;
			foreach (string item in array)
			{
				yield return new Pair<string, int?>(item, null);
			}
		}
		if (msg.UnavailableResearchIds != null)
		{
			Pair<string, int>[] unavailableResearchIds = msg.UnavailableResearchIds;
			for (int i = 0; i < unavailableResearchIds.Length; i++)
			{
				Pair<string, int> pair = unavailableResearchIds[i];
				yield return new Pair<string, int?>(pair.Item1, pair.Item2);
			}
		}
	}
}
