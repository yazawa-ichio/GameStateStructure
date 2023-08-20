using System;

namespace GameStateStructure
{
	// デコレーター
	public class DecoratorAttribute : Attribute
	{
		public Type Decorator { get; private set; }

		public DecoratorAttribute(Type decorator)
		{
			Decorator = decorator;
		}
	}

	public class Decorator
	{

	}

}