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
	[string]$ProjectOrSolution = "WebsiteKiosk.csproj",
	[string]$ArtifactsDirectory = "artifacts",
	[switch]$ManageIisSite,
	[string]$IisSiteName = "WebsiteKiosk",
	[switch]$SkipGitHubRelease,
	[switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
	param(
		[Parameter(Mandatory = $true)]
		[string]$Message
	)

	Write-Host "[Create-Release] $Message" -ForegroundColor Cyan
}

function Stop-IisSiteIfRequested {
	param(
		[Parameter(Mandatory = $true)]
		[string]$SiteName
	)

	if (-not $ManageIisSite) {
		return $false
	}

	if ($DryRun) {
		Write-Host "[DRYRUN] Stop-Website -Name '$SiteName'"
		return $true
	}

	$stopWebsiteCommand = Get-Command Stop-Website -ErrorAction SilentlyContinue
	if (-not $stopWebsiteCommand) {
		Write-Warning "IIS cmdlets not available. Install/enable IIS management tools or run without -ManageIisSite."
		return $false
	}

	$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
	if (-not $site) {
		Write-Warning "IIS site '$SiteName' was not found."
		return $false
	}

	if ($site.State -eq "Stopped") {
		Write-Step "IIS site '$SiteName' is already stopped."
		return $true
	}

	Write-Step "Stopping IIS site '$SiteName'..."
	Stop-Website -Name $SiteName
	return $true
}

function Start-IisSiteIfRequested {
	param(
		[Parameter(Mandatory = $true)]
		[string]$SiteName,
		[Parameter(Mandatory = $true)]
		[bool]$ShouldStart
	)

	if (-not $ShouldStart) {
		return
	}

	if ($DryRun) {
		Write-Host "[DRYRUN] Start-Website -Name '$SiteName'"
		return
	}

	$startWebsiteCommand = Get-Command Start-Website -ErrorAction SilentlyContinue
	if (-not $startWebsiteCommand) {
		Write-Warning "IIS cmdlets not available; unable to restart IIS site '$SiteName'."
		return
	}

	$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
	if (-not $site) {
		Write-Warning "IIS site '$SiteName' was not found during restart."
		return
	}

	if ($site.State -eq "Started") {
		Write-Step "IIS site '$SiteName' is already started."
		return
	}

	Write-Step "Starting IIS site '$SiteName'..."
	Start-Website -Name $SiteName
}

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

	# Use Start-Process for live console output and reliable exit-code handling.
	$process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru
	if ($process.ExitCode -ne 0) {
		throw "Command failed: $display (exit code $($process.ExitCode))"
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
$iisSiteManaged = $false
try {
	# Ensure script is running inside a git work tree.
	Write-Step "Validating git repository context..."
	Invoke-ExternalCommand -FilePath "git" -Arguments @("rev-parse", "--is-inside-work-tree") -CaptureOutput | Out-Null

	# Require a clean working tree so releases are reproducible.
	Write-Step "Checking working tree is clean..."
	$status = Invoke-ExternalCommand -FilePath "git" -Arguments @("status", "--porcelain") -CaptureOutput
	if (@($status).Count -gt 0) {
		throw "Working tree is not clean. Commit or stash changes before running this script."
	}

	# Compute next semantic version tag from existing tags.
	Write-Step "Calculating next semantic version tag..."
	$allTags = Invoke-ExternalCommand -FilePath "git" -Arguments @("tag", "--list", "v*") -CaptureOutput
	$newTag = Get-NextVersionTag -Tags $allTags -Increment $Increment

	Write-Host "Next version tag: $newTag"

	# Create and push the new git tag.
	Write-Step "Creating git tag '$newTag'..."
	Invoke-ExternalCommand -FilePath "git" -Arguments @("tag", $newTag)
	Write-Step "Pushing git tag '$newTag' to remote '$Remote'..."
	Invoke-ExternalCommand -FilePath "git" -Arguments @("push", $Remote, $newTag)

	# Build output paths for publish and release artifact.
	$artifactsRoot = Join-Path $repoRoot $ArtifactsDirectory
	$publishDirectory = Join-Path $artifactsRoot "website"
	$zipPath = Join-Path $artifactsRoot "WebsiteKiosk-web-$newTag.zip"

	# Prepare artifacts directories.
	Write-Step "Preparing artifact folders..."
	if (-not $DryRun) {
		New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
		if (Test-Path $publishDirectory) {
			Remove-Item $publishDirectory -Recurse -Force
		}
	}

	# Optionally stop IIS site to avoid file lock conflicts during publish.
	$iisSiteManaged = Stop-IisSiteIfRequested -SiteName $IisSiteName

	# Publish release build.
	Write-Step "Publishing release build from '$ProjectOrSolution'..."
	Invoke-ExternalCommand -FilePath "dotnet" -Arguments @("publish", $ProjectOrSolution, "-c", "Release", "-o", $publishDirectory)

	# Validate expected static site output exists.
	$zipSource = Join-Path $publishDirectory "wwwroot"
	if (-not $DryRun -and -not (Test-Path $zipSource)) {
		throw "Expected publish output folder not found: $zipSource"
	}

	# Package published website files into versioned zip artifact.
	Write-Step "Packaging website files into '$zipPath'..."
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
		Write-Step "Preparing GitHub release upload..."
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
			try {
				Invoke-ExternalCommand -FilePath "gh" -Arguments @("release", "view", $newTag, "--repo", $repoSlug)
				$releaseExists = $true
			}
			catch {
				$releaseExists = $false
			}
		}

		# Upload artifact to existing release, or create a new release with notes.
		if ($releaseExists) {
			Write-Step "Uploading asset to existing GitHub release '$newTag'..."
			Invoke-ExternalCommand -FilePath "gh" -Arguments @("release", "upload", $newTag, $zipPath, "--repo", $repoSlug, "--clobber")
		}
		else {
			Write-Step "Creating GitHub release '$newTag' and uploading asset..."
			Invoke-ExternalCommand -FilePath "gh" -Arguments @("release", "create", $newTag, $zipPath, "--repo", $repoSlug, "--title", $newTag, "--generate-notes")
		}
	}
	else {
		Write-Step "Skipping GitHub release creation/upload (SkipGitHubRelease=true)."
	}

	Write-Step "Release automation complete."
	Write-Host "Release automation complete."
	Write-Host "Tag: $newTag"
	Write-Host "Zip: $zipPath"
}
finally {
	Start-IisSiteIfRequested -SiteName $IisSiteName -ShouldStart $iisSiteManaged

	# Restore original working directory.
	Pop-Location
}