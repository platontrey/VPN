// Node Configuration API Routes
// Add these to your API service router

// GET /api/v1/nodes/{id}/config
app.get('/api/v1/nodes/:id/config', async (req, res) => {
  try {
    const { id } = req.params;

    // Get node config via orchestrator gRPC
    const config = await orchestratorClient.getNodeConfig(id);

    res.json({
      success: true,
      data: config
    });
  } catch (error) {
    res.status(500).json({
      success: false,
      error: error.message
    });
  }
});

// PUT /api/v1/nodes/{id}/config
app.put('/api/v1/nodes/:id/config', async (req, res) => {
  try {
    const { id } = req.params;
    const { hysteriaConfig } = req.body;

    // Validate config (basic validation)
    if (!hysteriaConfig || typeof hysteriaConfig !== 'object') {
      return res.status(400).json({
        success: false,
        error: 'Invalid Hysteria2 configuration'
      });
    }

    // Send config update via orchestrator gRPC
    await orchestratorClient.updateNodeConfig(id, hysteriaConfig);

    res.json({
      success: true,
      message: 'Configuration updated successfully'
    });
  } catch (error) {
    res.status(500).json({
      success: false,
      error: error.message
    });
  }
});