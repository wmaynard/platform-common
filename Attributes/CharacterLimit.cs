using System;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Property)]
public class CharacterLimit : Attribute
{
    public int Length { get; init; }
    
    public CharacterLimit(int length) => Length = length;
}