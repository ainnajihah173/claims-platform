# Assumptions

1. Exposure = sum of reserves on open claims, grouped by market + currency + type.
2. No group-wide currency conversion (needs FX rates — out of scope).
3. Six markets = seeded market codes, each with one currency.
4. Login = fake login with seeded users; real JWT; ownership enforced.
5. No policy system — `policyNumber` is text.
6. Managers are staff: they can act on claims like officers and also see dashboards; "team" is a user field.
7. Decisions reach claimants as in-app notifications (no email/SMS).
8. Overdue targets: motor 14 days, property 30 (config). Clock does **not** pause during AwaitingInfo (later improvement).
9. Out of scope: attachments, payments, approval limits.
