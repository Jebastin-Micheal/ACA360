using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACA360.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class DoNotUppercaseAttribute : Attribute
    {
        // Empty marker class
    }
}
