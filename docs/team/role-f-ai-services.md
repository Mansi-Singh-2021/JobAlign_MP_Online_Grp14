# Role F — AI service client: extraction, feedback, summaries

**Story:** US-05b + US-08 · **Points:** 7 · **Branch:** `feat/f-ai-services`

> Read [00-SHARED-BRIEF.md](00-SHARED-BRIEF.md) and [01-CONTRACTS.md](01-CONTRACTS.md) first.

---

## What you own

The only code in the project that talks to an external AI service. You implement two
interfaces other people defined — `IJobExtractor` (A's) and `IFeedbackGenerator` — so your
work drops in behind stubs that already exist and already work.

**That is your safety net and your constraint.** Nobody is blocked waiting for you: the stub
extractor keeps the demo alive. In exchange, you must not change either interface to suit
your provider. If the shape genuinely does not fit, raise it with A and the lead.

## Requirements

| ID | Requirement | Pri |
|---|---|---|
| FR-12 | Extract title, company, location, work mode, experience, salary, responsibilities | M |
| FR-13 | Extract skills and classify each required or preferred | M |
| FR-17 | Record anything not stated as "Not specified"; never invent | M |
| FR-20 | Record a confidence indicator per extracted detail; flag low confidence | S |
| FR-44 | Generate written feedback describing strengths and gaps | S |
| FR-48 | Generate a short summary of a lengthy job description | S |

Business rules: **BR-02** (never invent). NFRs: **NFR-01** (extraction within 10s for 95%),
**NFR-06** (unavailability never loses a posting), **NFR-08** (config version stored),
**NFR-09** (send only what is required), **NFR-11** (provider replaceable),
**NFR-13** (extract once, store).

## Files you own

Create:
```
src/JobAlign.Infrastructure/Ai/AiClientOptions.cs
src/JobAlign.Infrastructure/Ai/AnthropicClient.cs
src/JobAlign.Infrastructure/Ai/ExtractionPrompt.cs
src/JobAlign.Infrastructure/Ai/AiExtractor.cs
src/JobAlign.Infrastructure/Ai/AiFeedbackGenerator.cs
src/JobAlign.Infrastructure/Ai/Dtos/AiExtractionResponse.cs
tests/JobAlign.Tests/AiExtractorTests.cs
```

Edit (announce first):
- `src/JobAlign.Infrastructure/DependencyInjection.cs` — swap the two stub registrations
- `src/JobAlign.Web/appsettings.json` — model name and base URL **only, never the key**

**Do not touch:** `IJobExtractor` or `IFeedbackGenerator` (contracts), `ExtractionService`
(A's), scoring, views. You write no UI.

---

## Provider

Use the **Anthropic Messages API** over `HttpClient`. There is no official .NET SDK; a typed
`HttpClient` is the right amount of machinery.

```
POST https://api.anthropic.com/v1/messages
x-api-key: <key from user-secrets>
anthropic-version: 2023-06-01
content-type: application/json

{ "model": "claude-sonnet-5", "max_tokens": 2048,
  "system": "<the extraction contract>",
  "messages": [ { "role": "user", "content": "<raw posting text>" } ] }
```

`claude-sonnet-5` is the right default here — strong extraction quality at sensible cost and
latency for NFR-01.

### The API key — read this twice

**The key goes in user-secrets, never in `appsettings.json`, never in a commit.**

```bash
dotnet user-secrets init --project src/JobAlign.Web
```

```bash
dotnet user-secrets set "Ai:ApiKey" "sk-ant-your-key-here" --project src/JobAlign.Web
```

`appsettings.json` may hold `Ai:Model` and `Ai:BaseUrl`. If a key ever reaches a commit,
tell the lead immediately and rotate it — it is public the moment it is pushed.

Bind with the options pattern:

```csharp
public sealed class AiClientOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-5";
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public int TimeoutSeconds { get; set; } = 30;
}
```

**If `ApiKey` is missing, fall back to the stub rather than crashing.** Five teammates
without a key still need the app to run.

---

## Task list

### 1. The extraction contract (system prompt)

Straight from `CLAUDE.md` — one call per posting, strict JSON, no prose, no markdown fences.
The system prompt must state:

- Return `null` for anything not explicitly stated — **never estimate** (BR-02, FR-17)
- Normalize salary to a yearly figure, but return the originally stated value and period too
- `"unclear"` is a valid and correct answer for `remotePolicy`
- Classify each skill as `required` or `preferred`
- Give a confidence of `high`, `medium` or `low` per field

Enums — reject anything else:

| Field | Allowed |
|---|---|
| `remotePolicy` | `remote` \| `hybrid` \| `onsite` \| `unclear` |
| `salaryPeriod` | `year` \| `month` \| `hour` \| `null` |
| `skillType` | `required` \| `preferred` |
| `confidence` | `high` \| `medium` \| `low` |

Set `ConfigVersion` to something like `"anthropic-sonnet5-v1"` and **bump it whenever you
change the prompt.** It is stored on every run so a result can be explained against the
prompt that produced it (NFR-08).

### 2. Parse defensively — AI output is untrusted input

**Never deserialize straight onto `PostingExtraction`** (shared brief, rule 6). Deserialize
to `AiExtractionResponse` in `Ai/Dtos/`, then validate, then map to A's `ExtractedPosting`.

Validate every field:

- Unknown enum value -> treat as null, do not throw
- Negative or absurd salary or years -> null
- Model wrapped the JSON in ```` ```json ```` fences despite instructions -> strip and retry
  the parse before failing
- Empty skill name -> drop that skill
- **A string like `"Not specified"`, `"N/A"` or `"unknown"` must become `null`**, not be
  stored as text. Models do this often and it silently violates BR-02.

### 3. Failure handling (NFR-06, FR-19)

`ExtractAsync` **never throws for a provider problem.** Return `ExtractionOutcome.Failure`
with a useful reason for every one of: timeout, non-2xx, unparseable JSON, missing key,
rate limit. A's `ExtractionService` turns that into a `Pending` posting with the reason
recorded, and the posting survives — that is NFR-06.

One retry on a timeout or a 429/5xx, with a short backoff. Not more; NFR-01 gives you 10
seconds.

### 4. Privacy (NFR-09)

Send **only the raw posting text**. Never the candidate's name, email, profile or resume.
For feedback, `FeedbackRequest` already contains only skill names and a score — do not
enrich it.

### 5. Feedback generation (FR-44)

`AiFeedbackGenerator` takes a `FeedbackRequest` and returns a short paragraph naming
strengths and the main gaps. The SRS has a worked example of the tone:

> "You have a strong foundation for this position and meet the major backend development
> requirements. Docker and Azure are the main skill gaps. Prioritizing these technologies
> would improve your suitability for similar .NET roles."

Two or three sentences. Concrete about which skills. No invented facts about the candidate.

Store it on `MatchResult.FeedbackText` with `FeedbackGeneratedAt`. **Generated once, then
stored** — viewing a posting must never trigger a call (NFR-13). Regenerate only when the
score changes materially or the candidate asks.

Coordinate with D: D owns `MatchResult` upserts and has been told not to blank
`FeedbackText`. Write only those two fields.

### 6. Summaries (FR-48) — do last

`PostingExtraction.Summary`, populated in the same extraction call rather than a second one.
Lowest priority in your slice; cut it if day 2 is tight.

---

## Acceptance criteria

- [ ] With a valid key, pasting a real posting produces genuinely extracted detail
- [ ] Unstated details come back `null`, and never the string "Not specified"
- [ ] `remotePolicy` of `unclear` is stored as `Unclear`, distinct from null
- [ ] Skills are classified required vs preferred
- [ ] Confidence is recorded per field
- [ ] With **no** key configured, the app runs on the stub and nothing crashes
- [ ] With a deliberately wrong key, the posting survives as `Pending` with the reason shown
- [ ] A malformed or fenced JSON response degrades to a Failure outcome, not an exception
- [ ] Extraction completes within 10 seconds for a typical posting (NFR-01)
- [ ] No API key anywhere in git
- [ ] Feedback reads naturally and names real skills from the gap list
- [ ] Viewing a posting twice makes zero additional API calls (NFR-13)

## Tests to write

Do **not** call the real API in tests. Inject a fake `HttpMessageHandler` returning canned
responses.

```
AiExtractor_maps_a_well_formed_response_to_ExtractedPosting
AiExtractor_converts_the_literal_string_Not_specified_to_null
AiExtractor_maps_unclear_remote_policy_to_Unclear_not_null
AiExtractor_rejects_an_unknown_enum_value_without_throwing
AiExtractor_returns_Failure_on_a_timeout
AiExtractor_returns_Failure_on_a_non_success_status
AiExtractor_returns_Failure_on_unparseable_json
AiExtractor_strips_markdown_fences_before_parsing
AiExtractor_drops_a_skill_with_an_empty_name
AiFeedbackGenerator_returns_null_when_the_provider_is_unavailable
```

## Dependencies

| You need | From | Until then |
|---|---|---|
| `IJobExtractor`, `ExtractedPosting` | A (Wave 0) | Blocked until Wave 0 — spend the time on the prompt and the DTO |
| `IFeedbackGenerator`, `FeedbackRequest` | Wave 0 | same |
| `MatchResult` with gaps | D | Test feedback against a hand-built `FeedbackRequest` |
| An API key | The lead | Get this sorted on day 1 morning, not day 2 |

**Nobody is blocked on you** — the stubs cover the demo. That makes yours the slice to cut
if the team runs out of time, and it is why the fallback must keep working. Never remove the
stub registration; swap it conditionally on the key being present.

## Day-2 swap

```csharp
// DependencyInjection.cs — replaces the two Wave 0 stub lines
services.AddScoped<IJobExtractor>(sp =>
    string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<AiClientOptions>>().Value.ApiKey)
        ? sp.GetRequiredService<StubExtractor>()      // no key: demo still works
        : sp.GetRequiredService<AiExtractor>());
```

Register both concrete types so either can be resolved. Announce the swap before you push —
it changes behaviour for everyone.
