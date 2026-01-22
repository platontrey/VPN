// Add to orchestrator service handlers

func (h *NodeHandler) GetNodeConfig(ctx context.Context, req *pb.GetNodeConfigRequest) (*pb.GetNodeConfigResponse, error) {
    // Connect to node via gRPC
    conn, err := h.getNodeConnection(req.NodeId)
    if err != nil {
        return nil, err
    }
    defer conn.Close()

    client := pb.NewNodeManagerClient(conn)

    // Request current config from node
    resp, err := client.GetStatus(context.Background(), &pb.StatusRequest{})
    if err != nil {
        return nil, err
    }

    // Parse Hysteria2 config from response
    config := resp.HysteriaConfig
    if config == "" {
        config = "{}" // Default empty config
    }

    return &pb.GetNodeConfigResponse{
        Config: config,
    }, nil
}

func (h *NodeHandler) UpdateNodeConfig(ctx context.Context, req *pb.UpdateNodeConfigRequest) (*pb.UpdateNodeConfigResponse, error) {
    // Connect to node via gRPC
    conn, err := h.getNodeConnection(req.NodeId)
    if err != nil {
        return nil, err
    }
    defer conn.Close()

    client := pb.NewNodeManagerClient(conn)

    // Send config update to node
    _, err = client.UpdateConfig(context.Background(), &pb.ConfigUpdateRequest{
        Config: req.Config,
    })
    if err != nil {
        return nil, err
    }

    // Request reload
    _, err = client.ReloadConfig(context.Background(), &pb.ReloadRequest{})
    if err != nil {
        return nil, err
    }

    return &pb.UpdateNodeConfigResponse{
        Success: true,
    }, nil
}