/// Production-ready defaults and convenience helpers for Farmer resources
[<AutoOpen>]
module Farmer.ProductionDefaults

open Farmer
open Farmer.Arm.Web

/// Application Insights production defaults
module AppInsights =
    /// Production-optimized sampling (20%) - keeps all errors, samples successes
    /// Recommended for high-traffic applications to reduce costs while maintaining error visibility
    let productionSampling (config: AppInsightsConfig) = {
        config with
            SamplingPercentage = 20
    }

    /// Development sampling (100%) - keeps all telemetry
    /// Useful for local/dev environments where cost is not a concern
    let developmentSampling (config: AppInsightsConfig) = {
        config with
            SamplingPercentage = 100
    }

    /// Recommended production configuration
    let production = appInsights { sampling_percentage 20 }

    /// Development configuration
    let development = appInsights { sampling_percentage 100 }

/// Azure Functions production defaults
module Functions =
    /// Applies production-ready defaults to a Functions configuration
    /// - AlwaysOn (if supported by SKU)
    /// - HTTPS enforcement
    /// - Scale limit of 100 (for Consumption plan)
    let applyProductionDefaults (config: FunctionsConfig) : FunctionsConfig =
        let isConsumptionPlan =
            match config.CommonWebConfig.Sku with
            | Sku.Y1 -> true
            | _ -> false

        let scaleLimit =
            match config.FunctionAppScaleLimit with
            | None when isConsumptionPlan -> Some 100 // Prevent runaway costs
            | existing -> existing

        {
            config with
                CommonWebConfig = {
                    config.CommonWebConfig with
                        AlwaysOn =
                            if isConsumptionPlan then
                                false // Not supported on Consumption
                            else
                                true // Enable for Premium/Dedicated
                        HTTPSOnly = true
                }
                FunctionAppScaleLimit = scaleLimit
        }

    /// Applies development-friendly defaults
    /// - Lower scale limit (10) to reduce dev costs
    /// - AlwaysOn disabled to save costs
    let applyDevelopmentDefaults (config: FunctionsConfig) : FunctionsConfig =
        let isConsumptionPlan =
            match config.CommonWebConfig.Sku with
            | Sku.Y1 -> true
            | _ -> false

        {
            config with
                CommonWebConfig = {
                    config.CommonWebConfig with
                        AlwaysOn = false // Save costs in dev
                        HTTPSOnly = true // Still enforce HTTPS
                }
                FunctionAppScaleLimit =
                    if isConsumptionPlan then
                        Some 10 // Lower limit for dev
                    else
                        None
        }

/// Recommended production configurations by resource type
module Presets =
    /// High-traffic production API
    let highTrafficApi =
        functions {
            service_plan_sku Sku.EP1 // Premium plan
            max_scale_out_limit 200
            always_on
            https_only
            enable_managed_identity
        }

    /// Standard production workload
    let standardProduction =
        functions {
            service_plan_sku Sku.Y1 // Consumption
            max_scale_out_limit 100
            https_only
            enable_managed_identity
        }

    /// Development/staging environment
    let development =
        functions {
            service_plan_sku Sku.Y1 // Consumption
            max_scale_out_limit 10
            https_only
        }

    /// Cost-optimized production (infrequent traffic)
    let costOptimized =
        functions {
            service_plan_sku Sku.Y1 // Consumption
            max_scale_out_limit 50
            https_only
            enable_managed_identity
        }
