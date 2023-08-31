using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GameStateStructure
{
	class MakerAttributeCache<TMaker>
	{
		static Dictionary<Type, MakerAttributeCache<TMaker>> s_Cache = new Dictionary<Type, MakerAttributeCache<TMaker>>();

		public static MakerAttributeCache<TMaker> Get(Type type)
		{
			if (s_Cache.TryGetValue(type, out var cache))
			{
				return cache;
			}
			return s_Cache[type] = new MakerAttributeCache<TMaker>(type);
		}

		public readonly TMaker Maker;
		public readonly TMaker[] Makers;

		private MakerAttributeCache(Type type)
		{
			var attrs = type.GetCustomAttributes(typeof(TMaker)).Cast<TMaker>().ToArray();
			if (attrs.Length > 0)
			{
				Maker = attrs[0];
				Makers = attrs;
			}
		}
	}

}