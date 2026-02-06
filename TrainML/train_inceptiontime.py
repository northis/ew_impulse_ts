import argparse
import csv
import os
from dataclasses import dataclass
import onnx

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader, TensorDataset, random_split


@dataclass
class TrainConfig:
    data_path: str
    output_dir: str
    epochs: int
    batch_size: int
    learning_rate: float
    val_split: float
    seed: int


class InceptionBlock(nn.Module):
    def __init__(self, in_channels: int, out_channels: int, kernel_sizes: list[int]):
        super().__init__()
        self.branches = nn.ModuleList(
            [
                nn.Conv1d(in_channels, out_channels, k, padding=k // 2)
                for k in kernel_sizes
            ]
        )
        self.bn = nn.BatchNorm1d(out_channels * len(kernel_sizes))
        self.relu = nn.ReLU()

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        outputs = [branch(x) for branch in self.branches]
        x = torch.cat(outputs, dim=1)
        x = self.bn(x)
        return self.relu(x)


class InceptionTime(nn.Module):
    def __init__(self, in_channels: int, num_classes: int):
        super().__init__()
        self.block1 = InceptionBlock(in_channels, 32, [9, 19, 39])
        self.block2 = InceptionBlock(96, 32, [9, 19, 39])
        self.block3 = InceptionBlock(96, 32, [9, 19, 39])
        self.pool = nn.AdaptiveAvgPool1d(1)
        self.fc = nn.Linear(96, num_classes)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = self.block1(x)
        x = self.block2(x)
        x = self.block3(x)
        x = self.pool(x).squeeze(-1)
        return self.fc(x)


def read_csv_dataset(path: str) -> tuple[np.ndarray, np.ndarray]:
    with open(path, newline="", encoding="utf-8") as handle:
        reader = csv.reader(handle)
        header = next(reader)
        rows = list(reader)

    labels = np.array([int(row[0]) for row in rows], dtype=np.int64)
    features = np.array([[float(v) for v in row[2:]] for row in rows], dtype=np.float32)
    return features, labels


def build_dataloader(features: np.ndarray, labels: np.ndarray, batch_size: int, val_split: float, seed: int):
    feature_count = features.shape[1]
    bars_count = feature_count // 4
    tensor_x = torch.from_numpy(features.reshape(-1, 4, bars_count))
    tensor_y = torch.from_numpy(labels)

    dataset = TensorDataset(tensor_x, tensor_y)
    val_size = int(len(dataset) * val_split)
    train_size = len(dataset) - val_size
    generator = torch.Generator().manual_seed(seed)
    train_ds, val_ds = random_split(dataset, [train_size, val_size], generator=generator)

    train_loader = DataLoader(train_ds, batch_size=batch_size, shuffle=True)
    val_loader = DataLoader(val_ds, batch_size=batch_size, shuffle=False)
    return train_loader, val_loader, bars_count


def train(config: TrainConfig):
    torch.manual_seed(config.seed)
    features, labels = read_csv_dataset(config.data_path)
    train_loader, val_loader, bars_count = build_dataloader(
        features, labels, config.batch_size, config.val_split, config.seed
    )

    model = InceptionTime(in_channels=4, num_classes=2)
    optimizer = torch.optim.Adam(model.parameters(), lr=config.learning_rate)
    loss_fn = nn.CrossEntropyLoss()

    best_val = float("inf")
    os.makedirs(config.output_dir, exist_ok=True)

    for epoch in range(config.epochs):
        model.train()
        for batch_x, batch_y in train_loader:
            optimizer.zero_grad()
            logits = model(batch_x)
            loss = loss_fn(logits, batch_y)
            loss.backward()
            optimizer.step()

        model.eval()
        val_loss = 0.0
        val_count = 0
        with torch.no_grad():
            for batch_x, batch_y in val_loader:
                logits = model(batch_x)
                loss = loss_fn(logits, batch_y)
                val_loss += loss.item() * batch_y.size(0)
                val_count += batch_y.size(0)

        val_loss /= max(1, val_count)
        print(f"Epoch {epoch + 1}/{config.epochs} - val loss: {val_loss:.6f}")

        if val_loss < best_val:
            best_val = val_loss
            model_path = os.path.join(config.output_dir, "inceptiontime.pt")
            torch.save(model.state_dict(), model_path)

    
    onnx_path = os.path.join(config.output_dir, "inceptiontime.onnx")

    dummy = torch.zeros(1, 4, bars_count, dtype=torch.float32)
    model.eval()
    torch.onnx.export(
        model,
        dummy,
        onnx_path,
        input_names=["input"],
        output_names=["logits"],
        opset_version=18,
        dynamic_axes={"input": {0: "batch"}, "logits": {0: "batch"}},
        do_constant_folding=True,
    )
    
    onnx_model = onnx.load(onnx_path)
    onnx.save_model(
        onnx_model,
        onnx_path,
        save_as_external_data=False
    )
    print(f"Saved ONNX model (a single file) to {onnx_path}")


def parse_args() -> TrainConfig:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", required=True, help="Path to dataset CSV")
    parser.add_argument("--out", default="model", help="Output directory")
    parser.add_argument("--epochs", type=int, default=10)
    parser.add_argument("--batch", type=int, default=32)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--val", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    return TrainConfig(
        data_path=args.data,
        output_dir=args.out,
        epochs=args.epochs,
        batch_size=args.batch,
        learning_rate=args.lr,
        val_split=args.val,
        seed=args.seed,
    )


if __name__ == "__main__":
    cfg = parse_args()
    train(cfg)
