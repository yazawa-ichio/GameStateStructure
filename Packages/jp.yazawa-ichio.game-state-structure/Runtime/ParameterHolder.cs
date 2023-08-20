using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameStateStructure
{
	public class ParameterHolder
	{
		Dictionary<string, object> m_Data = new Dictionary<string, object>();

		public void Set(string key, object value)
		{
			m_Data[key] = value;
		}

		internal void Apply(object obj)
		{
			StateInfo.Get(obj.GetType()).Apply(obj, m_Data);
		}

		class StateInfo
		{
			static Dictionary<Type, StateInfo> s_Cache = new();

			public static StateInfo Get(Type type)
			{
				if (!s_Cache.TryGetValue(type, out var data))
				{
					data = new StateInfo(type);
					s_Cache[type] = data;
				}
				return data;
			}

			class ArgData
			{
				public PropertyInfo Property { get; private set; }
				public FieldInfo Field { get; private set; }

				public ArgData(PropertyInfo property)
				{
					Property = property;
				}

				public ArgData(FieldInfo field)
				{
					Field = field;
				}

			}

			Dictionary<string, ArgData> m_Dic = new();

			public StateInfo(Type type)
			{
				foreach (var info in type.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
				{
					if (!info.IsDefined(typeof(ArgAttribute)))
					{
						continue;
					}
					m_Dic[info.Name] = new ArgData(info);
				}
				foreach (var info in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
				{
					if (!info.IsDefined(typeof(ArgAttribute)))
					{
						continue;
					}
					m_Dic[info.Name] = new ArgData(info);
				}
			}

			public void Apply(object obj, Dictionary<string, object> args)
			{
				foreach (var kvp in args)
				{
					if (m_Dic.TryGetValue(kvp.Key, out var data))
					{
						data.Property?.SetValue(obj, kvp.Value);
						data.Field?.SetValue(obj, kvp.Value);
					}
				}
			}
		}

	}

}