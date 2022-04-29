using System;
using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.Common.Attributes;

[AttributeUsage(validOn: AttributeTargets.Method)]
public class HealthMonitor : Attribute
{
	internal int Weight { get; init; }
	
	public HealthMonitor([Range(1, int.MaxValue, ErrorMessage = "Weight must be positive.")]int weight) => Weight = weight;
}