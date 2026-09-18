using ACA360.Core.Binders;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class UppercaseStringModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        if (context.Metadata.ModelType == typeof(string))
        {
            var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
            var fallbackBinder = new SimpleTypeModelBinder(context.Metadata.ModelType, loggerFactory);

            // Because this class is in the same namespace as the binder, 
            // it will find 'UppercaseStringModelBinder' perfectly now.
            return new UppercaseStringModelBinder(fallbackBinder);
        }

        return null;
    }
}