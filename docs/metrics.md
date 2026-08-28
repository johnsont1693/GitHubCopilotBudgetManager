# Metric catalog and interpretation

The built-in catalog is defined in `MetricCatalog.cs`. Every metric has a source, unit, direction, supported scope, and caveat. Policy JSON configures thresholds, windows, weights, and whether missing data is required.

Categories include financial consumption and forecast, seat activation, DAU/WAU/MAU adoption, interactions, code generation and acceptance, directional lines added, agent/chat/CLI adoption, AI credits, and data quality.

`Unknown` is a first-class status. Required missing, suppressed, stale, or future-dated observations never become an inferred Green, Yellow, or Red result. Teams omitted by GitHub's fewer-than-five-seat privacy rule remain Unknown.

Copilot usage and pull-request signals are associative, not causal productivity measurements. Lines of code, acceptance, prompts, and agent use must not be used as individual performance targets. The system does not provide a separate “Copilot impact API”; it consumes documented usage/billing APIs and stores explicit explanations.

Enterprise metrics de-duplicate users. Organization and team dimensions preserve GitHub attribution; users in multiple teams can appear in every team and team rows must not be summed back into organization totals.