using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ACA360.Core.Binders
{
    public class UppercaseStringModelBinder : IModelBinder
    {
        private readonly IModelBinder _fallbackBinder;

        public UppercaseStringModelBinder(IModelBinder fallbackBinder)
        {
            _fallbackBinder = fallbackBinder;
        }

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            bool shouldApply = false;

            // Extract the controller descriptor once to use it for multiple checks
            ControllerActionDescriptor controllerDescriptor = bindingContext.ActionContext.ActionDescriptor as ControllerActionDescriptor;

            // 1. Check if the Controller or Action has [ApplyUppercase]
            if (controllerDescriptor != null)
            {
                bool hasControllerAttribute = controllerDescriptor.ControllerTypeInfo
                    .GetCustomAttributes(typeof(ApplyUppercaseAttribute), true).Any();

                bool hasMethodAttribute = controllerDescriptor.MethodInfo
                    .GetCustomAttributes(typeof(ApplyUppercaseAttribute), true).Any();

                shouldApply = hasControllerAttribute || hasMethodAttribute;
            }

            if (!shouldApply)
            {
                return _fallbackBinder.BindModelAsync(bindingContext);
            }

            // 2A. SAFETY CHECK FOR C# CLASS PROPERTIES
            if (bindingContext.ModelMetadata.ContainerType != null &&
                !string.IsNullOrEmpty(bindingContext.ModelMetadata.PropertyName))
            {
                var propertyInfo = bindingContext.ModelMetadata.ContainerType
                    .GetProperty(bindingContext.ModelMetadata.PropertyName);

                if (propertyInfo != null)
                {
                    // Get all attributes on this specific property
                    var attributes = propertyInfo.GetCustomAttributes(true);

                    // Check if ANY attribute on this property contains the word "Encrypt" or "DoNotUppercase"
                    bool isProtected = attributes.Any(attr =>
                        attr.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase) ||
                        attr.GetType().Name.Contains("DoNotUppercase", StringComparison.OrdinalIgnoreCase)
                    );

                    if (isProtected)
                    {
                        return _fallbackBinder.BindModelAsync(bindingContext);
                    }
                }
            }

            // 2B. SAFETY CHECK FOR METHOD PARAMETERS (Fixes standalone 'string planId')
            if (controllerDescriptor != null)
            {
                var parameterInfo = controllerDescriptor.MethodInfo.GetParameters()
                    .FirstOrDefault(p => p.Name.Equals(bindingContext.ModelName, StringComparison.OrdinalIgnoreCase));

                if (parameterInfo != null)
                {
                    bool isProtectedParam = parameterInfo.GetCustomAttributes(true).Any(attr =>
                        attr.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase) ||
                        attr.GetType().Name.Contains("DoNotUppercase", StringComparison.OrdinalIgnoreCase)
                    );

                    if (isProtectedParam)
                    {
                        return _fallbackBinder.BindModelAsync(bindingContext);
                    }
                }
            }

            // 3. Fallback Name Check (Expanded to include planid and hash)
            var fieldName = bindingContext.ModelName.ToLower();
            if ( fieldName.Contains("password") || fieldName.Contains("token") || fieldName.Contains("hash"))
            {
                return _fallbackBinder.BindModelAsync(bindingContext);
            }

            // 4. Get the actual value submitted by the user/browser
            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            if (valueProviderResult != ValueProviderResult.None)
            {
                var value = valueProviderResult.FirstValue;

                if (!string.IsNullOrEmpty(value))
                {
                    // 5. THE ULTIMATE FAIL-SAFE: Base64 Signature Detection
                    // Encrypted hashes almost always end with '=' or '==' and contain '+' or '/'
                    if (value.EndsWith("=") || value.Contains("+") || value.Contains("/") || (value.Length > 40 && !value.Contains(" ")))
                    {
                        // It looks like an encrypted hash! Leave it exactly as it is.
                        return _fallbackBinder.BindModelAsync(bindingContext);
                    }

                    // 6. If it clears ALL safety checks, it is a normal string. Uppercase it.
                    bindingContext.Result = ModelBindingResult.Success(value.ToUpper());
                    return Task.CompletedTask;
                }
            }

            return _fallbackBinder.BindModelAsync(bindingContext);
        }
    }
}