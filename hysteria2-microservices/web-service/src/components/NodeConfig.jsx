import React, { useState, useEffect } from 'react';
import axios from 'axios';

const NodeConfig = ({ nodeId }) => {
  const [config, setConfig] = useState({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');

  useEffect(() => {
    loadConfig();
  }, [nodeId]);

  const loadConfig = async () => {
    try {
      const response = await axios.get(`/api/v1/nodes/${nodeId}/config`);
      setConfig(response.data.data);
    } catch (error) {
      setMessage('Failed to load configuration');
    } finally {
      setLoading(false);
    }
  };

  const saveConfig = async () => {
    setSaving(true);
    try {
      await axios.put(`/api/v1/nodes/${nodeId}/config`, {
        hysteriaConfig: config
      });
      setMessage('Configuration saved successfully');
    } catch (error) {
      setMessage('Failed to save configuration');
    } finally {
      setSaving(false);
    }
  };

  const updateConfig = (key, value) => {
    setConfig(prev => ({
      ...prev,
      [key]: value
    }));
  };

  if (loading) return <div>Loading configuration...</div>;

  return (
    <div className="node-config">
      <h3>Node Configuration</h3>

      {message && <div className="alert">{message}</div>}

      <div className="config-form">
        <div className="form-group">
          <label>Listen Port:</label>
          <input
            type="number"
            value={config.listen || ''}
            onChange={(e) => updateConfig('listen', parseInt(e.target.value))}
          />
        </div>

        <div className="form-group">
          <label>Obfs:</label>
          <input
            type="text"
            value={config.obfs || ''}
            onChange={(e) => updateConfig('obfs', e.target.value)}
          />
        </div>

        <div className="form-group">
          <label>Obfs Password:</label>
          <input
            type="password"
            value={config['obfs-password'] || ''}
            onChange={(e) => updateConfig('obfs-password', e.target.value)}
          />
        </div>

        <div className="form-group">
          <label>Auth:</label>
          <input
            type="text"
            value={config.auth || ''}
            onChange={(e) => updateConfig('auth', e.target.value)}
          />
        </div>

        <div className="form-group">
          <label>Auth Password:</label>
          <input
            type="password"
            value={config['auth-password'] || ''}
            onChange={(e) => updateConfig('auth-password', e.target.value)}
          />
        </div>

        <div className="form-group">
          <label>QUIC:</label>
          <textarea
            value={JSON.stringify(config.quic || {}, null, 2)}
            onChange={(e) => {
              try {
                updateConfig('quic', JSON.parse(e.target.value));
              } catch {}
            }}
            rows="4"
          />
        </div>

        <div className="form-group">
          <label>Bandwidth:</label>
          <textarea
            value={JSON.stringify(config.bandwidth || {}, null, 2)}
            onChange={(e) => {
              try {
                updateConfig('bandwidth', JSON.parse(e.target.value));
              } catch {}
            }}
            rows="4"
          />
        </div>
      </div>

      <button
        onClick={saveConfig}
        disabled={saving}
        className="save-btn"
      >
        {saving ? 'Saving...' : 'Save Configuration'}
      </button>

      <div className="raw-config">
        <h4>Raw JSON Config:</h4>
        <textarea
          value={JSON.stringify(config, null, 2)}
          onChange={(e) => {
            try {
              setConfig(JSON.parse(e.target.value));
            } catch {}
          }}
          rows="20"
        />
      </div>
    </div>
  );
};

export default NodeConfig;