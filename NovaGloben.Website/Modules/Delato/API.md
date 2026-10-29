# Delato API

Shared expenses for a group of people: who paid what, how it is split, and who should pay whom. The API answers on
the Delato site only, under `{site}/api/v1` (on a host, `/delato/api/v1/...`). Everywhere else these addresses
answer 404, and so do they on a host where Delato is off.

**On or off:** Delato is off unless the host sets `module.delato.enabled=true` (in the `FeatureManagement`
configuration). Off, its controllers leave the application and its site loses its aliases (its data stays).

## Sign-in

Every call except `GET currencies` needs a signed-in account of the `/delato` site, by its Stomme cookie or JWT.
Without one the host refuses it before Delato runs (measured: 403). A sign-in whose account no longer exists answers
401 `UNAUTHORIZED`.
- Registration is open, and an account signs in only once its e-mail is confirmed.
- A cookie-authenticated `POST`, `PUT` or `DELETE` must carry Stomme's antiforgery header (`X-XSRF-TOKEN-HEADER`,
  whose value is in the page's antiforgery token). Without it the call is refused.

## Money

- **USD is the one settlement currency.** Balances, pairwise debts, suggested transfers, "my summary" and settle-up
  are all in USD.
- **A group's `baseCurrency` is display-only.** It is never used to compute anything.
- **An expense keeps its original amount, its currency and its rate to USD** (`fxRateToUsd`, 1 unit = ? USD, at most 8
  decimals).
- `storageMode` (`ConvertedToUsd` or `KeepOriginal`) records the user's choice for a non-USD expense. It is
  optional: left out (or empty), it is `ConvertedToUsd`. Any other value is refused. A USD expense is always
  `KeepOriginal` at rate 1.
- **Every amount is a decimal**, written as a JSON number. Entered amounts have at most 2 decimals. Original amounts in
  zero-decimal currencies (IRR, JPY) are fine, because only the USD side is kept in cents.
- **The USD amount is the original amount times the rate, rounded half-up to the cent.**

## Splits

`splitMode` is `Equal`, `Shares` (positive whole numbers), `Percentage` (must sum to 100) or `ExactAmounts` (must sum
to the original amount). Targets are members or couples/subgroups (`Entity`).

The split sums exactly to the expense's USD amount:
1. Each share is floored to the cent.
2. The leftover cents go one by one in ascending target id order, Member before Entity on an id tie.
3. An entity's share is divided equally among its members, its leftover cents going to the ascending member ids.

The test vectors in `Tests/split-test-vectors.json` are the specification.

## Members and accounts

A member is a name in a group, with an optional e-mail. Each signed-in account sees:
- the groups it created;
- the groups where it has a member: one claimed by its id, or an unclaimed one whose e-mail equals the account's
  **confirmed** e-mail. The first such visit claims the member: `userId` is set and `isMe` is true for that account.

**Any other group answers 404, exactly as a missing group does,** so ids (which are sequential) tell a stranger
nothing.

An account whose e-mail is not confirmed is never matched by e-mail, so it sees no group it did not create. A claimed
member is never claimed by another account.

A member's e-mail is shown only to the members of its group: in the group's detail and member list. It never appears
in the group list, the report or "my summary", or in any answer to someone outside the group.

## Endpoints

| Method and path | Answer |
|---|---|
| `POST groups` `{name}` | 201 group; the caller becomes its first member |
| `GET groups` | the caller's groups, with member count and the caller's net in USD |
| `GET/PUT/DELETE groups/{id}` | detail (members and entities) / rename / delete (creator only; another member gets 403) |
| `POST/GET groups/{id}/members`, `DELETE .../members/{memberId}` | add `{name, email?}` (409 if the e-mail is already a member) / list / remove (409 `MEMBER_HAS_BALANCE` or `MEMBER_HAS_HISTORY`) |
| `POST/GET groups/{id}/entities`, `DELETE .../entities/{entityId}` | couple (exactly 2) or subgroup (2 or more); a member is in at most one couple; 409 if an expense uses it |
| `POST/GET groups/{id}/expenses`, `GET/PUT/DELETE .../expenses/{expenseId}` | expenses, newest date first; an edit recomputes the shares |
| `POST/GET groups/{id}/settlements`, `DELETE .../settlements/{settlementId}` | a payment between two members, in any currency with its rate, counted in USD |
| `GET groups/{id}/report` | totals, member and entity balances, pairwise debts, suggested transfers (members and entities), all USD |
| `GET me/summary` | across the caller's groups: whom to pay and who pays the caller, in USD |
| `GET currencies` | the currencies an expense or payment may be entered in (anonymous) |
| `POST import` | the one-time import of an earlier ledger: the site's Host or Administrators only (see below) |

## Import

`POST import` brings a whole earlier ledger in, once. It is for the `/delato` site's **Host or Administrators** only,
and it needs the antiforgery header like every cookie write.

**The payload** is `{groups, expenses, settlements}`, each record with its own earlier id:
- groups carry their members (name, optional e-mail) and couples/subgroups, and `creatorMemberId`;
- expenses carry their targets and their shares, as computed then;
- every amount is a JSON number, and `kind`, `type`, `storageMode` and `splitMode` are their exact names.

The import's rules:
- **All or nothing.** The whole payload is checked before anything is stored. The first bad row answers 400
  `VALIDATION`, named by table, index, id and column (never by its values), and nothing is written. Among the checks:
  every reference stays inside its group, a couple has 2 members, the shares sum to the USD amount, and the USD amount
  is the original amount times the rate, half-up to the cent.
- **Create if absent.** A record already on the site is skipped and listed in the answer's `skipped`, never
  overwritten. A second run of the same payload creates nothing, and nothing a member changed since is undone.
- **Never onto the site's own records.** An id used by a group the site made itself (or by one of its members,
  couples/subgroups, expenses or settlements) answers 409 `CONFLICT`, and nothing is written.
- **Ids continue.** The site's id counters are raised past every imported id, so the next new record follows.
- **No accounts are imported.** A member is claimed by the account whose **confirmed** e-mail is the member's. A
  group's creator becomes the account that claims the member its creator was; until then the group has no creator
  account (`createdByUserId` 0, which matches no one).
- Imported records keep their own creation times (UTC).

**One step for the owner of the earlier phone app.** Before that app is uninstalled, open it and note any expense
marked pending. Those exist only on the phone; they were never sent, so no import can carry them.

## Errors

Errors answer `{"error": {"code", "message"}}`. The codes:

| Code | Status |
|---|---|
| `VALIDATION` | 400 |
| `UNAUTHORIZED` | 401 |
| `FORBIDDEN` | 403 |
| `GROUP_NOT_FOUND` (missing, or not the caller's), `NOT_FOUND` | 404 |
| `CONFLICT`, `SPLIT_MISMATCH`, `MEMBER_HAS_BALANCE`, `MEMBER_HAS_HISTORY` | 409 |

## Running it

**Writes are serialised within the process** (ids are issued once, and a group is written by one call at a time). The
service must therefore run as **one process**. Do not scale it out until the store has concurrency of its own.
