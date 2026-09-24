using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;

namespace Durango.UI;

public class UriMethods : IUriInvokable
{
	private struct UriMethod
	{
		public string[] Tokens;

		public MethodInfo Method;

		public int ParamCount;
	}

	private readonly List<UriMethod> _methods = new List<UriMethod>();

	private readonly object _parent;

	public UriMethods([NotNull] object parent)
	{
		_parent = parent;
		Type type = parent.GetType();
		BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		while (type != null)
		{
			MethodInfo[] methods = type.GetMethods(bindingFlags);
			foreach (MethodInfo methodInfo in methods)
			{
				object[] customAttributes = methodInfo.GetCustomAttributes(typeof(UriAttribute), inherit: false);
				if (customAttributes.Length == 0)
				{
					continue;
				}
				object[] array = customAttributes;
				foreach (object obj in array)
				{
					UriAttribute uriAttribute = (UriAttribute)obj;
					string[] tokens = ((!string.IsNullOrEmpty(uriAttribute.Key)) ? uriAttribute.Key.Split(UriParser.Separator, StringSplitOptions.RemoveEmptyEntries) : null);
					int paramCount = KUtility.GetSize(methodInfo.GetParameters());
					if (_methods.FindIndex(delegate(UriMethod o)
					{
						if (o.ParamCount != paramCount)
						{
							return false;
						}
						if (KUtility.GetSize(tokens) != KUtility.GetSize(o.Tokens))
						{
							return false;
						}
						return (tokens == null || tokens.SequenceEqual(o.Tokens)) ? true : false;
					}) == -1)
					{
						_methods.Add(new UriMethod
						{
							Tokens = tokens,
							Method = methodInfo,
							ParamCount = paramCount
						});
					}
				}
			}
			type = type.BaseType;
			bindingFlags &= ~BindingFlags.Public;
		}
		_methods.Sort(delegate(UriMethod m1, UriMethod m2)
		{
			int num = KUtility.GetSize(m2.Tokens) - KUtility.GetSize(m1.Tokens);
			if (num == 0)
			{
				num = m2.ParamCount - m1.ParamCount;
			}
			return num;
		});
	}

	public int InvokeUri(string[] tokens, int start)
	{
		int num = KUtility.GetSize(tokens) - start;
		int num2 = -1;
		for (int i = 0; i < _methods.Count; i++)
		{
			UriMethod uriMethod = _methods[i];
			int size = KUtility.GetSize(uriMethod.Tokens);
			if (size + uriMethod.ParamCount != num)
			{
				continue;
			}
			bool flag = true;
			for (int j = 0; j < size; j++)
			{
				if (!uriMethod.Tokens[j].Equals(tokens[start + j], StringComparison.OrdinalIgnoreCase))
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				num2 = i;
				break;
			}
		}
		if (num2 == -1)
		{
			return 0;
		}
		UriMethod uriMethod2 = _methods[num2];
		object[] array = new object[uriMethod2.ParamCount];
		int size2 = KUtility.GetSize(uriMethod2.Tokens);
		for (int k = 0; k < uriMethod2.ParamCount; k++)
		{
			array[k] = tokens[start + size2 + k];
		}
		uriMethod2.Method.Invoke(_parent, array);
		return size2 + uriMethod2.ParamCount;
	}

	public IEnumerable<string> CollectUri()
	{
		for (int i = 0; i < _methods.Count; i++)
		{
			UriMethod uriMethod = _methods[i];
			string text = ((KUtility.GetSize(uriMethod.Tokens) != 0) ? string.Join("/", uriMethod.Tokens) : string.Empty);
			if (uriMethod.ParamCount > 0)
			{
				ParameterInfo[] parameters = uriMethod.Method.GetParameters();
				foreach (ParameterInfo parameterInfo in parameters)
				{
					string text2 = "{" + parameterInfo.Name + "}";
					if (text.Length > 0)
					{
						text += "/";
					}
					text += text2;
				}
			}
			yield return text;
		}
	}
}
