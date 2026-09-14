param(
    [ValidateSet('pad', 'liftoff', 'clouds', 'coast', 'orbit')][string]$Scene = 'pad',
    [ValidateRange(0.5, 1.0)][double]$Scale = 0.75,
    [ValidateRange(20, 240)][int]$Samples = 60,
    [bool]$GeometryAntialiasing = $false,
    [ValidateSet(2, 4, 8)][int]$GeometrySamples = 2,
    [ValidateSet('Disabled', 'Smaa', 'Fxaa')][string]$EdgeAA = 'Disabled',
    [string]$Bridge = 'http://localhost:9080'
)

$ErrorActionPreference = 'Stop'
$artifactDirectory = Join-Path $PSScriptRoot '../game/.artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$scaleText = $Scale.ToString([Globalization.CultureInfo]::InvariantCulture)

# Drive an already-running development instance; never mix captures with timed samples.
Invoke-RestMethod "$Bridge/control?restart=true&pause=false&throttle=0" | Out-Null
$msaaCount = if ($GeometryAntialiasing) { $GeometrySamples } else { 0 }
Invoke-RestMethod "$Bridge/render?scale=$scaleText&samples=$msaaCount&edgeAA=$EdgeAA&reconstruction=Fsr2&reflection=true" | Out-Null
Invoke-RestMethod "$Bridge/camera?look=12&bearing=295&distance=95" | Out-Null
switch ($Scene) {

    'clouds' {

        Invoke-RestMethod "$Bridge/control?altitude=2400&speed=0&pause=true" | Out-Null
        Invoke-RestMethod "$Bridge/camera?look=8&bearing=250&distance=160" | Out-Null

    }
    'coast' {

        Invoke-RestMethod "$Bridge/control?altitude=800&latitude=28.52&longitude=-80.57&speed=0&pause=true" | Out-Null
        Invoke-RestMethod "$Bridge/camera?look=35&bearing=90&distance=400" | Out-Null

    }
    'orbit' {

        Invoke-RestMethod "$Bridge/control?altitude=120000&speed=0&pause=true" | Out-Null
        Invoke-RestMethod "$Bridge/camera?look=60&bearing=250&distance=80000" | Out-Null

    }

}
$settled = $false

for ($attempt = 0; $attempt -lt 60; $attempt++) {

    Start-Sleep -Milliseconds 500
    $state = Invoke-RestMethod "$Bridge/state"

    if ($state.terrainPendingJobs -eq 0 -and $state.scatterPending -eq 0 -and $state.forestPendingJobs -eq 0 -and $state.cloudTexturesReady -ne $false) {

        $settled = $true
        break

    }

}

if (-not $settled) { throw 'Scene streaming and cloud textures did not settle within 30 seconds.' }
Start-Sleep -Seconds 8
Invoke-RestMethod "$Bridge/render?resetTimings=true" | Out-Null

if ($Scene -eq 'liftoff') {

    Invoke-RestMethod "$Bridge/control?throttle=1&hold=Ascent" | Out-Null

}

$readings = @(for ($sampleIndex = 0; $sampleIndex -lt $Samples; $sampleIndex++) {

    Invoke-RestMethod "$Bridge/state"
    Start-Sleep -Milliseconds 200

})

$last = $readings[-1]
$summary = [pscustomobject]@{

    Scene = $Scene
    RenderScale = $Scale
    Reconstruction = 'Fsr2'
    GeometryAntialiasing = $GeometryAntialiasing
    GeometrySamples = $msaaCount
    EdgeAA = $EdgeAA
    CloudSteps = $last.cloudSteps
    Width = $last.windowWidth
    Height = $last.windowHeight
    Samples = $Samples
    GpuMeanMs = ($readings | Measure-Object renderGpuMs -Average).Average
    GpuMaxMs = ($readings | Measure-Object renderGpuMs -Maximum).Maximum
    UpdateMeanMs = ($readings | Measure-Object updateMs -Average).Average
    HudMeanMs = ($readings | Measure-Object hudMs -Average).Average
    LastFps = $last.fps
    FrameP95Ms = $last.frameP95Ms
    FrameP99Ms = $last.frameP99Ms
    FrameSamples = $last.frameSamples
    Altitude = $last.altitude

}

$file = Join-Path $artifactDirectory "benchmark-$Scene-$scaleText.json"
[pscustomobject]@{ Summary = $summary; Samples = $readings } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file
$summary | ConvertTo-Json
