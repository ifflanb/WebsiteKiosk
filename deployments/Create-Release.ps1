[CmdletBinding()]
# Script parameters:
# - Increment: semantic version bump type (patch/minor/major)
# - Remote: git remote to push tag/release against
# - ProjectOrSolution: .sln/.csproj to publish
# - ArtifactsDirectory: output folder for publish + zip
# - SkipGitHubRelease: skip gh release create/upload
# - DryRun: print commands without making changes
param(
	[ValidateSet("patch", "minor", "major")]
	[string]$Increment = "patch",
	[string]$Remote = "origin",
	[string]$ProjectOrSolution = "WebsiteKiosk.slnx",
	[string]$ArtifactsDirectory = "artifacts",
	[switch]$SkipGitHubRelease,
	[switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Runs external commands consistently, with optional output capture and dry-run support.
function Invoke-ExternalCommand {
	param(
		[Parameter(Mandatory = $true)]
		[string]$FilePath,
		[Parameter(Mandatory = $true)]
		[string[]]$Arguments,
		[switch]$CaptureOutput
	)

	$display = "$FilePath $($Arguments -join ' ')"
	if ($DryRun) {
		Write-Host "[DRYRUN] $display"
		if ($CaptureOutput) {
			return @()
		}

		return
	}

	if ($CaptureOutput) {
		$output = & $FilePath @Arguments 2>&1
		if ($LASTEXITCODE -ne 0) {
			throw "Command failed: $display`n$output"
		}

		return $output
	}

	# Use Start-Process to avoid PowerShell treating native stderr text as exceptions.
	$stdoutFile = [System.IO.Path]::GetTempFileName()
	$stderrFile = [System.IO.Path]::GetTempFileName()
	try {
		$process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile

		$stdoutLines = if (Test-Path $stdoutFile) { Get-Content -Path $stdoutFile } else { @() }
		$stderrLines = if (Test-Path $stderrFile) { Get-Content -Path $stderrFile } else { @() }

		$stdoutLines | ForEach-Object { Write-Host $_ }
		$stderrLines | ForEach-Object { Write-Host $_ }

		if ($process.ExitCode -ne 0) {
			$errorText = ($stderrLines -join [Environment]::NewLine)
			throw "Command failed: $display`n$errorText"
		}
	}
	finally {
		Remove-Item $stdoutFile -ErrorAction SilentlyContinue
		Remove-Item $stderrFile -ErrorAction SilentlyContinue
	}
}

# Parses a GitHub owner/repo slug from a git remote URL.
function Get-RepoSlugFromRemoteUrl {
	param(
		[Parameter(Mandatory = $true)]
		[string]$RemoteUrl
	)

	if ($RemoteUrl -match "github\.com[:/](?<slug>[^/]+/[^/.]+)(\.git)?$") {
		return $Matches.slug
	}

	throw "Unable to parse GitHub owner/repo from remote URL: $RemoteUrl"
}

# Finds the latest vX.Y.Z tag and returns the next version tag based on Increment.
function Get-NextVersionTag {
	param(
		[Parameter(Mandatory = $true)]
		[string[]]$Tags,
		[Parameter(Mandatory = $true)]
		[ValidateSet("patch", "minor", "major")]
		[string]$Increment
	)

	$parsedTags = foreach ($tag in $Tags) {
		if ($tag -match "^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)$") {
			[pscustomobject]@{
				Tag = $tag
				Major = [int]$Matches.major
				Minor = [int]$Matches.minor
				Patch = [int]$Matches.patch
			}
		}
	}

	if (-not $parsedTags -or @($parsedTags).Count -eq 0) {
		return "v0.1.0"
	}

	$latest = $parsedTags |
		Sort-Object -Property @{ Expression = "Major"; Descending = $true }, @{ Expression = "Minor"; Descending = $true }, @{ Expression = "Patch"; Descending = $true } |
		Select-Object -First 1

	$major = $latest.Major
	$minor = $latest.Minor
	$patch = $latest.Patch

	switch ($Increment) {
		"major" {
			$major += 1
			$minor = 0
			$patch = 0
		}
		"minor" {
			$minor += 1
			$patch = 0
		}
		default {
			$patch += 1
		}
	}

	return "v$major.$minor.$patch"
}

# Resolve repository root from this script location.
$scriptDirectory = Split-Path -Path $PSCommandPath -Parent
$repoRoot = Resolve-Path (Join-Path $scriptDirectory "..")

Push-Location $repoRoot
try {
	# Ensure script is running inside a git work tree.
	Invoke-ExternalCommand -FilePath "git" -Arguments @("rev-parse", "--is-inside-work-tree") -CaptureOutput | Out-Null

	# Require a clean working tree so releases are reproducible.
	$status = Invoke-ExternalCommand -FilePath "git" -Arguments @("status", "--porcelain") -CaptureOutput
	if (@($status).Count -gt 0) {
		throw "Working tree is not clean. Commit or stash changes before running this script."
	}

	# Compute next semantic version tag from existing tags.
	$allTags = Invoke-ExternalCommand -FilePath "git" -Arguments @("tag", "--list", "v*") -CaptureOutput
	$newTag = Get-NextVersionTag -Tags $allTags -Increment $Increment

	Write-Host "Next version tag: $newTag"

	# Create and push the new git tag.
	Invoke-ExternalCommand -FilePath "git" -Arguments @("tag", $newTag)
	Invoke-ExternalCommand -FilePath "git" -Arguments @("push", $Remote, $newTag)

	# Build output paths for publish and release artifact.
	$artifactsRoot = Join-Path $repoRoot $ArtifactsDirectory
	$publishDirectory = Join-Path $artifactsRoot "website"
	$zipPath = Join-Path $artifactsRoot "WebsiteKiosk-web-$newTag.zip"

	# Prepare artifacts directories.
	if (-not $DryRun) {
		New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
		if (Test-Path $publishDirectory) {
			Remove-Item $publishDirectory -Recurse -Force
		}
	}

	# Publish release build.
	Invoke-ExternalCommand -FilePath "dotnet" -Arguments @("publish", $ProjectOrSolution, "-c", "Release", "-o", $publishDirectory)

	# Validate expected static site output exists.
	$zipSource = Join-Path $publishDirectory "wwwroot"
	if (-not $DryRun -and -not (Test-Path $zipSource)) {
		throw "Expected publish output folder not found: $zipSource"
	}

	# Package published website files into versioned zip artifact.
	if ($DryRun) {
		Write-Host "[DRYRUN] Compress-Archive -Path '$zipSource\*' -DestinationPath '$zipPath' -Force"
	}
	else {
		if (Test-Path $zipPath) {
			Remove-Item $zipPath -Force
		}

		Compress-Archive -Path (Join-Path $zipSource "*") -DestinationPath $zipPath -Force
	}

	# Optionally create/update GitHub release and upload the zip asset.
	if (-not $SkipGitHubRelease) {
		# Verify GitHub CLI availability.
		$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
		if (-not $ghCommand) {
			throw "GitHub CLI ('gh') is not installed or not on PATH. Install it or run with -SkipGitHubRelease."
		}

		# Derive target GitHub repo from selected git remote.
		$remoteUrl = (Invoke-ExternalCommand -FilePath "git" -Arguments @("remote", "get-url", $Remote) -CaptureOutput | Select-Object -First 1).ToString().Trim()
		$repoSlug = Get-RepoSlugFromRemoteUrl -RemoteUrl $remoteUrl

		# Check whether release already exists.
		$releaseExists = $false
		if (-not $DryRun) {
			& gh release view $newTag --repo $repoSlug *> $null
			$releaseExists = ($LASTEXITCODE -eq 0)
		}

		# Upload artifact to existing release, or create a new release with notes.
		if ($releaseExists) {
			Invoke-ExternalCommand -FilePath "gh" -Arguments @("release", "upload", $newTag, $zipPath, "--repo", $repoSlug, "--clobber")
		}
		else {
			Invoke-ExternalCommand -FilePath "gh" -Arguments @("release", "create", $newTag, $zipPath, "--repo", $repoSlug, "--title", $newTag, "--generate-notes")
		}
	}

	Write-Host "Release automation complete."
	Write-Host "Tag: $newTag"
	Write-Host "Zip: $zipPath"
}
finally {
	# Restore original working directory.
	Pop-Location
}