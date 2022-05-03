using System;

namespace Rumble.Platform.Common.Extensions;

// Refactored from https://stackoverflow.com/questions/129389/how-do-you-do-a-deep-copy-of-an-object-in-net
public static class ArrayExtension
{
	public static void ForEach(this Array array, Action<Array, int[]> action)
	{
		if (array.LongLength == 0) 
			return;
		ArrayTraverse walker = new ArrayTraverse(array);
		do
		{
			action(array, walker.Position);
		} while (walker.Step());
	}
}

internal class ArrayTraverse
{
	public readonly int[] Position;
	private readonly int[] maxLengths;

	public ArrayTraverse(Array array)
	{
		maxLengths = new int[array.Rank];
		for (int i = 0; i < array.Rank; i++)
			maxLengths[i] = array.GetLength(i) - 1;
		Position = new int[array.Rank];
	}

	public bool Step()
	{
		for (int i = 0; i < Position.Length; ++i)
		{
			if (Position[i] >= maxLengths[i]) 
				continue;
			Position[i]++;
			for (int j = 0; j < i; j++)
				Position[j] = 0;
			return true;
		}
		return false;
	}
}