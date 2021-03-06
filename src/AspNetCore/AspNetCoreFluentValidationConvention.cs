using System;
using System.Reflection;
using FluentValidation.AspNetCore;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rocket.Surgery.AspNetCore.FluentValidation;
using Rocket.Surgery.Conventions;
using Rocket.Surgery.Extensions.DependencyInjection;
using Rocket.Surgery.Extensions.FluentValidation;

[assembly: Convention(typeof(AspNetCoreFluentValidationConvention))]

namespace Rocket.Surgery.AspNetCore.FluentValidation
{
    /// <summary>
    /// AspNetCoreFluentValidationConvention.
    /// Implements the <see cref="IServiceConvention" />
    /// </summary>
    /// <seealso cref="IServiceConvention" />
    [PublicAPI]
    public class AspNetCoreFluentValidationConvention : IServiceConvention
    {
        private readonly FluentValidationMvcConfiguration _configuration;

        /// <summary>
        /// THe validation settings
        /// </summary>
        /// <param name="configuration"></param>
        public AspNetCoreFluentValidationConvention([CanBeNull] FluentValidationMvcConfiguration? configuration = null)
        {
            _configuration = configuration ?? new FluentValidationMvcConfiguration();
        }

        /// <summary>
        /// Registers the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        public void Register([NotNull] IServiceConventionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            context.Services
               .Configure<MvcOptions>(
                    options => { options.Filters.Insert(0, new ValidationExceptionFilter()); }
                )
               .Configure<JsonOptions>(
                    options => options.JsonSerializerOptions.Converters.Add(new ValidationProblemDetailsConverter())
                )
               .AddMvcCore()
               .AddFluentValidation(
                    config =>
                    {
                        foreach (var field in typeof(FluentValidationMvcConfiguration).GetFields(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        ))
                        {
                            field.SetValue(config, field.GetValue(_configuration));
                        }

                        config.ValidatorFactoryType ??= typeof(ValidatorFactory);
                    }
                );

            context.Services.AddSingleton<IValidatorInterceptor, ValidatorInterceptor>();
            context.Services.AddSingleton<ProblemDetailsFactory, FluentValidationProblemDetailsFactory>();
            context.Services.Configure<ApiBehaviorOptions>(
                o =>
                {
                    ProblemDetailsFactory? problemDetailsFactory = null;
                    o.InvalidModelStateResponseFactory = context =>
                    {
                        // ProblemDetailsFactory depends on the ApiBehaviorOptions instance. We intentionally avoid constructor injecting
                        // it in this options setup to to avoid a DI cycle.
                        problemDetailsFactory ??= context.HttpContext.RequestServices
                           .GetRequiredService<ProblemDetailsFactory>();
                        return problemDetailsInvalidModelStateResponse(problemDetailsFactory, context);
                    };

                    static IActionResult problemDetailsInvalidModelStateResponse(
                        ProblemDetailsFactory problemDetailsFactory,
                        ActionContext context
                    )
                    {
                        var problemDetails =
                            problemDetailsFactory.CreateValidationProblemDetails(
                                context.HttpContext,
                                context.ModelState
                            );
                        ObjectResult result;
                        if (problemDetails.Status == 400)
                        {
                            // For compatibility with 2.x, continue producing BadRequestObjectResult instances if the status code is 400.
                            result = new BadRequestObjectResult(problemDetails);
                        }
                        else
                        {
                            result = new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };
                        }

                        result.ContentTypes.Add("application/problem+json");
                        result.ContentTypes.Add("application/problem+xml");

                        return result;
                    }
                }
            );
        }
    }
}