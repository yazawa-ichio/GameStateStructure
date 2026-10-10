using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameStateStructure.Diagnostics
{
	public readonly struct EventEntry
	{
		public string Label { get; }

		public MethodInfo Method { get; }

		public ParameterInfo[] Parameters { get; }

		/// <summary>ツールから値を用意できる引数だけで構成されているか。</summary>
		public bool CanInvoke { get; }

		/// <summary>このイベントで遷移する先。宣言されていなければ null。</summary>
		public Type Transition { get; }

		/// <summary>発行関数を生成しない指定か。</summary>
		public bool IsInternal { get; }

		internal EventEntry(SubscribeEventAttribute attribute, MethodInfo method, bool viaInterface)
		{
			Method = method;
			Label = viaInterface
				? $"{method.DeclaringType?.Name}.{method.Name}"
				: method.Name;
			Parameters = method.GetParameters();
			Transition = attribute.Transition;
			IsInternal = attribute.Internal;
			var canInvoke = !method.IsGenericMethod;
			foreach (var parameter in Parameters)
			{
				if (!EventCatalog.IsSupportedParameter(parameter.ParameterType))
				{
					canInvoke = false;
					break;
				}
			}
			CanInvoke = canInvoke;
		}
	}

	/// <summary>ステートが公開しているイベント操作を列挙する。</summary>
	public static class EventCatalog
	{
		static readonly Dictionary<Type, EventEntry[]> s_Cache = new Dictionary<Type, EventEntry[]>();

		public static IReadOnlyList<EventEntry> Get(GameState state)
		{
			return state == null ? Array.Empty<EventEntry>() : Get(state.GetType());
		}

		public static IReadOnlyList<EventEntry> Get(Type type)
		{
			if (s_Cache.TryGetValue(type, out var cached))
			{
				return cached;
			}

			var list = new List<EventEntry>();
			var seen = new HashSet<string>();

			foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				var attribute = method.GetCustomAttribute<SubscribeEventAttribute>();
				if (attribute == null || !seen.Add(GetKey(method)))
				{
					continue;
				}
				list.Add(new EventEntry(attribute, method, false));
			}

			foreach (var interfaceType in type.GetInterfaces())
			{
				var onInterface = interfaceType.GetCustomAttribute<SubscribeEventAttribute>();
				foreach (var method in interfaceType.GetMethods())
				{
					var attribute = onInterface ?? method.GetCustomAttribute<SubscribeEventAttribute>();
					if (attribute == null || !seen.Add(GetKey(method)))
					{
						continue;
					}
					list.Add(new EventEntry(attribute, method, true));
				}
			}

			return s_Cache[type] = list.ToArray();
		}

		static string GetKey(MethodInfo method)
		{
			var parameters = method.GetParameters();
			var types = new string[parameters.Length];
			for (var i = 0; i < parameters.Length; i++)
			{
				types[i] = parameters[i].ParameterType.FullName;
			}
			return method.Name + "(" + string.Join(",", types) + ")";
		}

		/// <summary>ツールが入力欄を描ける型か。</summary>
		public static bool IsSupportedParameter(Type type)
		{
			return type == typeof(bool)
				|| type == typeof(int)
				|| type == typeof(float)
				|| type == typeof(string)
				|| type.IsEnum;
		}

		public static object CreateDefault(Type type)
		{
			if (type == typeof(string))
			{
				return string.Empty;
			}
			return type.IsValueType ? Activator.CreateInstance(type) : null;
		}

		public static void Invoke(GameState state, in EventEntry entry, object[] args)
		{
			entry.Method.Invoke(state, args);
		}
	}
}