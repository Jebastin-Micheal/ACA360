using System;

// This allows you to place the attribute on a whole Controller OR a specific Action method
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApplyUppercaseAttribute : Attribute
{
    // Empty marker class
}