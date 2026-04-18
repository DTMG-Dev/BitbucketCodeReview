# Code Review Guidelines (.NET)

## Purpose

This document defines how we review code in this .NET project.  
The goal is to improve software quality, reduce production risk, and help the team grow.

Code review is not just a syntax check. It validates behavior, architecture, security, performance, and long-term maintainability.

## Review Mindset

- Review the code, not the person.
- Prefer clear, actionable feedback.
- Explain the reason behind requested changes.
- Balance quality and delivery speed.
- If a change is correct but non-ideal, consider whether it blocks merge or can be a follow-up.

## Definition of Done for Review

A pull request is review-ready when:

- The change has a clear purpose and scope.
- The PR description explains what changed and why.
- Tests are added or updated for behavior changes.
- Build, lint, and tests pass in CI.
- No known critical issues remain unresolved.

## Severity Levels for Findings

Use clear severity to reduce ambiguity:

- `Blocker`: Must be fixed before merge (bugs, security, data loss, broken behavior).
- `Major`: Should be fixed before merge unless explicitly accepted with rationale.
- `Minor`: Improvement recommended, can be follow-up.
- `Nit`: Style/comment-level suggestion, non-blocking.

## Core Review Checklist

## 1) Correctness and Behavior

- Does the code do what the requirement says?
- Are edge cases handled (null, empty, invalid states, boundary values)?
- Are failure paths handled safely (timeouts, retries, external dependency failures)?
- Is there any behavior regression for existing flows?
- Are business rules implemented in the right layer (domain/application, not scattered)?

## 2) Architecture and Design

- Is the solution aligned with project architecture and patterns?
- Are abstractions justified (no unnecessary interfaces or indirection)?
- Is coupling low and cohesion high?
- Are dependencies oriented correctly (domain should not depend on infrastructure)?
- Is the change easy to extend without large rewrites?

## 3) .NET and C# Quality

- Naming is clear and consistent with C# conventions.
- Types and methods have single responsibility.
- Visibility is minimal (`private` by default, avoid overly broad `public` surface).
- `async/await` is used correctly:
  - No `async void` except event handlers.
  - Avoid `.Result` and `.Wait()` to prevent deadlocks.
  - Propagate `CancellationToken` where applicable.
- Exceptions are meaningful:
  - Do not swallow exceptions.
  - Catch specific exceptions where possible.
  - Add context when rethrowing.
- Avoid hidden side effects in property getters and simple methods.

## 4) Data Access and EF Core

- Queries are efficient and avoid N+1 issues.
- Only required fields are selected when possible.
- Correct tracking behavior is used (`AsNoTracking` for read-only queries).
- Transactions are used for multi-step write consistency.
- Migrations are safe, reversible where possible, and reviewed for production impact.
- Concurrency is considered where needed (row versioning, conflict handling).

## 5) API and Contract Safety

- Request/response contracts are backward compatible unless versioned.
- Validation is present at boundaries (DTOs, command/query inputs).
- HTTP status codes are correct and consistent.
- Error responses are structured and do not leak sensitive internals.
- Idempotency is considered for retry-prone operations.

## 6) Security

- No secrets or credentials in code, logs, or config committed to repo.
- Input validation and output encoding are appropriate.
- Authentication and authorization checks are in the right place and enforced.
- Sensitive data access follows least privilege.
- Logging does not expose PII/secrets.
- Dependency updates are reviewed for known vulnerabilities.

## 7) Performance and Scalability

- Hot paths avoid unnecessary allocations and repeated work.
- Expensive operations are not done in loops accidentally.
- Caching strategy is correct and invalidation is considered.
- External calls are bounded with timeout/retry/circuit-breaker strategy where relevant.
- Large payloads/streams are handled efficiently (avoid loading entire content into memory unnecessarily).

## 8) Observability and Operations

- Logs are structured and actionable (include correlation/context IDs where relevant).
- Metrics and tracing are added/updated for critical paths.
- Health checks and operational diagnostics remain accurate.
- Feature flags are used safely (default states and rollback plan considered).

## 9) Tests

- Tests cover expected behavior and key edge cases.
- Tests fail for the right reason and are deterministic.
- Unit tests focus on logic; integration tests cover IO boundaries.
- Assertions are meaningful, not just line coverage.
- New behavior has tests; removed behavior has tests removed/updated.

## 10) Readability and Maintainability

- Code is easy to understand without deep mental simulation.
- Complex logic is broken into smaller named methods.
- Dead code and commented-out blocks are removed.
- Comments explain "why", not obvious "what".
- Magic values are replaced with named constants/config where appropriate.

## Pull Request Expectations (Author)

Each PR should include:

- Problem statement and intent.
- Scope and non-goals.
- Risk assessment (what can break).
- Test evidence (unit/integration/manual).
- Deployment or rollback notes when relevant.
- Screenshots or sample payloads for API/UI behavior changes.

Keep PRs small and focused when possible. Large PRs reduce review quality.

## Reviewer Workflow

1. Read PR description and linked issue/context.
2. Scan architecture impact first (boundaries, dependency direction).
3. Validate correctness and failure paths.
4. Review tests and missing test scenarios.
5. Check non-functional areas: security, performance, observability.
6. Leave clear comments with severity and suggested direction.
7. Re-review only changed parts after updates unless risk requires full pass.

## Comment Style Guide

Prefer this format for actionable feedback:

- `Issue`: What is wrong.
- `Impact`: Why it matters.
- `Suggestion`: How to fix or improve.

Example:

- Issue: `GetCustomerAsync` blocks with `.Result`.
- Impact: Can deadlock under ASP.NET synchronization context and reduces throughput.
- Suggestion: Make call chain async and `await` task; pass `CancellationToken`.

## Common .NET Anti-Patterns to Catch

- Synchronous blocking of asynchronous code.
- Catch-all exceptions with no handling strategy.
- Entity models leaked directly through API contracts.
- Business logic in controllers instead of application/domain layer.
- Overuse of static/global state.
- Creating `HttpClient` per request instead of factory-managed lifetime.
- Missing cancellation support in long-running or IO-bound operations.

## Merge Decision Guidance

- Approve when code is correct, safe, and maintainable for current scope.
- Request changes for blocker/major issues.
- Use follow-up tickets for non-critical improvements.
- If trade-offs are accepted, document rationale in PR discussion.

## Team Standards Alignment

These guidelines complement:

- Repository coding standards.
- Architecture decision records (ADRs).
- Security and compliance requirements.
- CI/CD quality gates.

When standards conflict, follow the stricter requirement and raise it for team alignment.
