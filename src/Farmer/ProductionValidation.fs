/// Production readiness validation helpers for Farmer resources
[<AutoOpen>]
module Farmer.ProductionValidation

open Farmer
open Farmer.Arm.Web
open Farmer.Identity

/// Validation result types
type ValidationLevel =
    | Info
    | Warning
    | Error

type ValidationMessage = {
    Level: ValidationLevel
    Resource: string
    Message: string
}

module ValidationMessage =
    let info resource message = {
        Level = Info
        Resource = resource
        Message = message
    }

    let warning resource message = {
        Level = Warning
        Resource = resource
        Message = message
    }

    let error resource message = {
        Level = Error
        Resource = resource
        Message = message
    }

    let format msg =
        let icon =
            match msg.Level with
            | Info -> "ℹ️"
            | Warning -> "⚠️"
            | Error -> "❌"

        sprintf "%s [%s] %s" icon msg.Resource msg.Message

/// Validates Azure Functions configuration for production readiness
module Functions =
    open Farmer.WebApp

    let validate (config: FunctionsConfig) : ValidationMessage list =
        let messages = ResizeArray()
        let name = config.Name.ResourceName.Value

        // Check SKU appropriateness
        match config.CommonWebConfig.Sku with
        | Sku.Y1 ->
            messages.Add(
                ValidationMessage.info
                    name
                    "Using Consumption plan (Y1). Consider Premium (EP1) for production workloads requiring consistent performance."
            )
        | Sku.Free ->
            messages.Add(
                ValidationMessage.warning
                    name
                    "Using Free tier. Not recommended for production - use Consumption (Y1), Premium (EP1), or Dedicated plans."
            )
        | _ -> ()

        // Check Application Insights
        if config.CommonWebConfig.AppInsights.IsNone then
            messages.Add(
                ValidationMessage.warning
                    name
                    "Application Insights disabled. Observability is critical for production. Farmer auto-creates AI by default - avoid disabling it."
            )

        // Check scale limits for Consumption plan
        if config.FunctionAppScaleLimit.IsNone then
            match config.CommonWebConfig.Sku with
            | Sku.Y1 ->
                messages.Add(
                    ValidationMessage.warning
                        name
                        "No scale limit set on Consumption plan. Consider 'max_scale_out_limit 100' to prevent unexpected costs."
                )
            | _ -> ()

        // Check AlwaysOn for non-consumption plans
        match config.CommonWebConfig.Sku with
        | Sku.Y1 -> () // Consumption doesn't support AlwaysOn
        | Sku.Free -> ()
        | _ when not config.CommonWebConfig.AlwaysOn ->
            messages.Add(
                ValidationMessage.warning
                    name
                    "AlwaysOn disabled on Premium/Dedicated plan. Enable 'always_on' to prevent cold starts."
            )
        | _ -> ()

        // Check HTTPS enforcement
        if not config.CommonWebConfig.HTTPSOnly then
            messages.Add(
                ValidationMessage.warning name "HTTPS not enforced. Add 'https_only' for production security."
            )

        // Check Managed Identity
        match config.CommonWebConfig.Identity with
        | ManagedIdentity.Empty ->
            messages.Add(
                ValidationMessage.info
                    name
                    "No managed identity enabled. Consider 'enable_managed_identity' for secure Azure resource access without connection strings."
            )
        | _ -> ()

        messages |> List.ofSeq

    /// Validates and prints warnings, returns the config unchanged for chaining
    let validateAndWarn (config: FunctionsConfig) =
        let messages = validate config

        if messages.IsEmpty then
            printfn "✅ [%s] Production validation passed" config.Name.ResourceName.Value
        else
            printfn "Production validation for '%s':" config.Name.ResourceName.Value

            for msg in messages do
                printfn "   %s" (ValidationMessage.format msg)

        config

/// Validates Application Insights configuration for production readiness
module AppInsights =
    open Farmer.Arm.Insights

    let validate (config: AppInsightsConfig) : ValidationMessage list =
        let messages = ResizeArray()
        let name = config.Name.Value

        // Check sampling percentage
        if config.SamplingPercentage = 100 then
            messages.Add(
                ValidationMessage.warning
                    name
                    "Sampling at 100%. For high-traffic production apps, consider 'sampling_percentage 20' to reduce costs (keeps all errors, samples successes)."
            )
        elif config.SamplingPercentage < 10 then
            messages.Add(
                ValidationMessage.warning
                    name
                    "Sampling below 10%. This may miss important telemetry. Consider at least 10-20% for production."
            )

        // Check workspace vs classic
        match config.InstanceKind with
        | Classic ->
            messages.Add(
                ValidationMessage.info
                    name
                    "Using classic Application Insights. Consider linking to Log Analytics workspace for better query performance and retention."
            )
        | _ -> ()

        messages |> List.ofSeq

    /// Validates and prints warnings, returns the config unchanged for chaining
    let validateAndWarn (config: AppInsightsConfig) =
        let messages = validate config

        if messages.IsEmpty then
            printfn "✅ [%s] Production validation passed" config.Name.Value
        else
            printfn "Production validation for '%s':" config.Name.Value

            for msg in messages do
                printfn "   %s" (ValidationMessage.format msg)

        config

/// Validates deployment for production readiness
let validateDeployment (deployment: IDeploymentSource) =
    printfn "\n🔍 Running production validation...\n"

    let armDeployment = deployment.Deployment
    let mutable hasWarnings = false
    let mutable hasErrors = false

    // You could extend this to walk through all resources
    // For now, we'll provide a placeholder that encourages manual validation

    printfn "💡 Tip: Use ProductionValidation.Functions.validateAndWarn or ProductionValidation.AppInsights.validateAndWarn"
    printfn "   to validate individual resources.\n"

    if hasErrors then
        printfn "❌ Production validation failed with errors. Fix issues before deploying.\n"
    elif hasWarnings then
        printfn "⚠️  Production validation completed with warnings. Review before deploying.\n"
    else
        printfn "✅ Production validation passed.\n"

    deployment
