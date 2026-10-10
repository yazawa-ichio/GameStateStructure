namespace GameStateStructure
{
	/// <summary>省略可能な引数。未指定と既定値そのものを区別するために使う。</summary>
	public readonly struct Option<T>
	{
		/// <summary>未指定を表す値。</summary>
		public static Option<T> None => default;

		public static implicit operator Option<T>(T value) => new Option<T>(value);

		readonly T m_Value;

		public bool HasValue { get; }

		/// <summary>指定された値。<see cref="HasValue"/> が false の場合は既定値。</summary>
		public T Value => m_Value;

		public Option(T value)
		{
			m_Value = value;
			HasValue = true;
		}

		public T GetValueOrDefault(T defaultValue = default)
		{
			return HasValue ? m_Value : defaultValue;
		}

		public override string ToString()
		{
			return HasValue ? $"Option({m_Value})" : "Option(None)";
		}
	}
}