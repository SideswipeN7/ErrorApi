# Enables the main-branch protection this repository is meant to run with: merges go through a PR and
# the `build` CI job must pass. GitHub only allows branch protection on public repositories (or with
# Pro), so run this once, right after making the repository public:
#
#   pwsh .github/enable-branch-protection.ps1
#
# enforce_admins stays false on purpose: the owner keeps a direct-push escape hatch, and a solo
# maintainer with zero required approvals can still merge their own PRs. Flip it to true when a second
# maintainer exists.

$protection = @'
{
  "required_status_checks": { "strict": false, "contexts": ["build"] },
  "enforce_admins": false,
  "required_pull_request_reviews": { "required_approving_review_count": 0 },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": true
}
'@

$tmp = New-TemporaryFile
Set-Content $tmp $protection -Encoding UTF8
gh api -X PUT "repos/SideswipeN7/ErrorApi/branches/main/protection" -H "Accept: application/vnd.github+json" --input $tmp
Remove-Item $tmp
Write-Host "Branch protection enabled: PRs required, 'build' must pass, force-pushes and deletion blocked."
